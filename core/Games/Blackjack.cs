namespace RippersCasino.Core.Games;

public sealed class BlackjackHand
{
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public bool Npc { get; set; }
    public long Bankroll { get; set; }
    public int Bet { get; set; }
    public List<int> Cards { get; set; } = [];
    public string Status { get; set; } = "playing";
    public long Profit { get; set; }
}
public sealed class BlackjackRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int[] Shoe { get; set; } = [];
    public int Drawn { get; set; }
    public List<int> Dealer { get; set; } = [];
    public List<BlackjackHand> Hands { get; set; } = [];
    public int Turn { get; set; }
    public string Phase { get; set; } = "playing";
    public long Deadline { get; set; }
}
public sealed record BlackjackHandView(int Position, string Name, bool Npc, int Bet, int[] Cards, int Total, string Status, long Profit);
public sealed record BlackjackView(string Id, string Phase, int Turn, long Deadline, int[] Dealer, int DealerTotal, BlackjackHandView[] Hands);

public static class Blackjack
{
    public const int MaxPlayers = 3;
    public static (int Total, bool Soft) Value(IEnumerable<int> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var card in cards)
        {
            if (card is < 0 or > 51)
                throw new ArgumentException("Invalid playing card.");
            var rank = Games.Cards.Rank(card);
            if (rank == 1)
            {
                aces++;
                total += 11;
            }
            else
                total += Math.Min(10, rank);
        }
        while (total > 21 && aces > 0)
        {
            total -= 10;
            aces--;
        }
        return (total, aces > 0);
    }
    public static bool Natural(IReadOnlyCollection<int> cards) => cards.Count == 2 && Value(cards).Total == 21;
    public static BlackjackRound Deal(List<BlackjackHand> players, IRandomSource random, int decks, long now, int turnSeconds)
    {
        if (players.Count is < 1 or > MaxPlayers || players.Any(hand => hand.Position is < 0 or >= MaxPlayers) || players.Select(hand => hand.Position).Distinct().Count() != players.Count)
            throw new ArgumentException("Blackjack has room for you and two rivals.");
        if (players.Any(hand => hand.Bet < 2 || hand.Bet % 2 != 0)) throw new ArgumentException("Choose an even wager of at least 2 chips. Blackjack pays 3:2.");
        var round = new BlackjackRound { Shoe = Games.Cards.Shuffle(random, decks), Hands = players.OrderBy(hand => hand.Position).ToList() };
        for (var i = 0; i < 2; i++)
        {
            foreach (var hand in round.Hands)
                hand.Cards.Add(Draw(round));
            round.Dealer.Add(Draw(round));
        }
        foreach (var hand in round.Hands)
            if (Natural(hand.Cards))
                hand.Status = "blackjack";
        if (Natural(round.Dealer))
            Settle(round);
        else
            Advance(round, now, turnSeconds);
        return round;
    }
    private static int Draw(BlackjackRound round)
    {
        if (round.Drawn >= round.Shoe.Length)
            throw new InvalidOperationException("The deck is unavailable. Close and reopen the casino.");
        return round.Shoe[round.Drawn++];
    }
    public static void Act(BlackjackRound round, int position, string action, long now, int turnSeconds)
    {
        if (round.Phase != "playing" || round.Hands[round.Turn].Position != position)
            throw new InvalidOperationException("Wait for your blackjack turn.");
        var hand = round.Hands[round.Turn];
        switch (action)
        {
            case "hit":
                hand.Cards.Add(Draw(round));
                var total = Value(hand.Cards).Total;
                if (total >= 21)
                    hand.Status = total > 21 ? "bust" : "stand";
                break;
            case "stand":
                hand.Status = "stand";
                break;
            case "double":
                if (hand.Cards.Count != 2 || hand.Bankroll < checked(hand.Bet * 2L))
                    throw new InvalidOperationException("Double requires two cards and enough table chips.");
                hand.Bet = checked(hand.Bet * 2);
                hand.Cards.Add(Draw(round));
                hand.Status = Value(hand.Cards).Total > 21 ? "bust" : "stand";
                break;
            default:
                throw new ArgumentException("Unknown blackjack action.");
        }
        Advance(round, now, turnSeconds);
    }
    public static string NpcDecision(BlackjackHand hand, int dealerUp)
    {
        var (total, soft) = Value(hand.Cards);
        var up = Math.Min(10, Games.Cards.Rank(dealerUp));
        var doubleDown = !soft && (total == 11 && up != 1 || total == 10 && up is >= 2 and <= 9 || total == 9 && up is >= 3 and <= 6)
            || soft && total is >= 16 and <= 18 && up is >= 4 and <= 6;
        if (doubleDown && hand.Cards.Count == 2 && hand.Bankroll >= 2L * hand.Bet)
            return "double";
        if (soft)
            return total >= 19 || total == 18 && up is >= 2 and <= 8 ? "stand" : "hit";
        return total >= 17 || total >= 13 && up is >= 2 and <= 6 || total == 12 && up is >= 4 and <= 6 ? "stand" : "hit";
    }
    private static void Advance(BlackjackRound round, long now, int turnSeconds)
    {
        while (round.Turn < round.Hands.Count)
        {
            var hand = round.Hands[round.Turn];
            if (hand.Status != "playing")
            {
                round.Turn++;
                continue;
            }
            if (!hand.Npc)
            {
                round.Deadline = checked(now + turnSeconds);
                return;
            }
            var action = NpcDecision(hand, round.Dealer[0]);
            if (action == "stand")
                hand.Status = "stand";
            else
            {
                if (action == "double")
                    hand.Bet *= 2;
                hand.Cards.Add(Draw(round));
                var total = Value(hand.Cards).Total;
                if (total >= 21 || action == "double")
                    hand.Status = total > 21 ? "bust" : "stand";
            }
        }
        if (round.Hands.Any(hand => hand.Status == "stand"))
            while (Value(round.Dealer).Total < 17)
                round.Dealer.Add(Draw(round));
        Settle(round);
    }
    private static void Settle(BlackjackRound round)
    {
        var dealer = Value(round.Dealer).Total;
        var natural = Natural(round.Dealer);
        foreach (var hand in round.Hands)
        {
            var total = Value(hand.Cards).Total;
            hand.Profit = total > 21 || natural && !Natural(hand.Cards) ? -hand.Bet
                : Natural(hand.Cards) ? natural ? 0 : checked(hand.Bet * 3L / 2)
                : dealer > 21 || total > dealer ? hand.Bet : total == dealer ? 0 : -hand.Bet;
            hand.Status = hand.Profit > 0 ? Natural(hand.Cards) ? "blackjack" : "win" : hand.Profit < 0 ? total > 21 ? "bust" : "lose" : "push";
        }
        round.Phase = "complete";
        round.Deadline = 0;
        round.Turn = -1;
    }
    public static void Validate(BlackjackRound round)
    {
        const int limit = MaxPlayers;
        if (!Guid.TryParseExact(round.Id, "N", out _) || round.Shoe == null || round.Hands == null || round.Dealer == null ||
            round.Shoe.Length is < 52 or > 416 || round.Shoe.Length % 52 != 0 || round.Hands.Count < 1 || round.Hands.Count > limit || round.Dealer.Count < 2 ||
            round.Hands.Select(hand => hand.Position).Distinct().Count() != round.Hands.Count ||
            round.Hands.Any(hand => hand.Position < 0 || hand.Position >= limit || hand.Cards == null || hand.Cards.Count < 2 || hand.Bet < 2 || hand.Bet % 2 != 0 || hand.Bankroll < hand.Bet) ||
            round.Shoe.Any(card => card is < 0 or > 51) || round.Shoe.GroupBy(card => card).Any(group => group.Count() != round.Shoe.Length / 52) ||
            round.Shoe.Distinct().Count() != 52)
            throw new InvalidDataException("Your saved hand could not be restored. Close the casino and check the log.");
        var dealt = round.Hands.SelectMany(hand => hand.Cards).Concat(round.Dealer).Order().ToArray();
        if (round.Drawn != dealt.Length || round.Drawn > round.Shoe.Length || !round.Shoe.Take(round.Drawn).Order().SequenceEqual(dealt) ||
            round.Phase is not ("playing" or "complete") || round.Phase == "playing" && (round.Turn < 0 || round.Turn >= round.Hands.Count || round.Deadline < 1 || round.Hands[round.Turn].Npc || round.Hands[round.Turn].Status != "playing") ||
            round.Phase == "complete" && (round.Turn != -1 || round.Deadline != 0))
            throw new InvalidDataException("Your saved hand could not be restored. Close the casino and check the log.");
    }
    public static BlackjackView View(BlackjackRound round)
    {
        var complete = round.Phase == "complete";
        return new(round.Id, round.Phase, round.Turn < 0 ? -1 : round.Hands[round.Turn].Position, round.Deadline,
            complete ? round.Dealer.ToArray() : [round.Dealer[0], -1], complete ? Value(round.Dealer).Total : 0,
            round.Hands.Select(hand => new BlackjackHandView(hand.Position, hand.Name, hand.Npc, hand.Bet, hand.Cards.ToArray(), Value(hand.Cards).Total, hand.Status, hand.Profit)).ToArray());
    }
}
