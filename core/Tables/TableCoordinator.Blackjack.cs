using RippersCasino.Core.Games;

namespace RippersCasino.Core.Tables;

public sealed partial class TableCoordinator
{
    private static readonly string[] Gamblers = ["Misha / Scav", "Berkut / USEC", "Sova / BEAR", "Vovchik / Scav", "Granit / BEAR", "Kedr / USEC"];
    private void StartBlackjack(CasinoTable table, string playerId, int wager, List<TableWalletChange> changes)
    {
        if (table.Game != "blackjack" || table.Phase != "waiting" || table.OwnerId != playerId)
            throw new InvalidOperationException("Blackjack is not ready for a new hand.");
        if (wager < 2 || wager % 2 != 0 || wager > options.MaxWager || table.Seats.Any(seat => seat.Stack < wager))
            throw new InvalidOperationException("Choose an even wager within your table bankroll.");
        var liability = checked(wager * 2L);
        foreach (var seat in table.Seats)
        {
            var account = wallets.Read(seat.PlayerId);
            if (!account.Available || account.Chips > account.Limit - account.Held - liability)
                throw new InvalidOperationException("Withdraw some chips or choose a smaller wager.");
        }
        foreach (var seat in table.Seats)
        {
            seat.Liability = liability;
            changes.Add(new(seat.PlayerId, 0, 0, liability));
        }
        var hands = table.Seats.Select(seat => new BlackjackHand { Position = seat.Position, Name = seat.Name, Bankroll = seat.Stack, Bet = wager }).ToList();
        if (hands.Count == 1)
        {
            var previous = table.Blackjack?.Hands.Where(hand => hand.Npc).ToArray();
            if (previous?.Length > 0)
            {
                foreach (var npc in previous)
                    hands.Add(new()
                    {
                        Position = npc.Position,
                        Name = npc.Name,
                        Npc = true,
                        Bankroll = Math.Max(wager * 2L, npc.Bankroll + npc.Profit),
                        Bet = wager
                    });
            }
            else
            {
                var count = Math.Min(table.Capacity - 1, random.Next(2) + 1);
                var names = Gamblers.OrderBy(_ => random.Next(int.MaxValue)).Take(count).ToArray();
                foreach (var name in names)
                    hands.Add(new()
                    {
                        Position = Enumerable.Range(0, table.Capacity).First(position => hands.All(hand => hand.Position != position)),
                        Name = name,
                        Npc = true,
                        Bankroll = wager * 100L,
                        Bet = wager
                    });
            }
        }
        table.Phase = "playing";
        table.Blackjack = Games.Blackjack.Deal(hands, random, options.BlackjackDecks, clock(), options.TurnSeconds);
        FinishBlackjack(table, changes);
    }
    private void ActBlackjack(CasinoTable table, string playerId, string action, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, playerId);
        if (table.Game != "blackjack" || table.Phase != "playing" || table.Blackjack == null || seat.LeaveAfterRound)
            throw new InvalidOperationException("You have no active Blackjack hand.");
        Games.Blackjack.Act(table.Blackjack, seat.Position, action, clock(), options.TurnSeconds);
        FinishBlackjack(table, changes);
    }
    private void LeaveBlackjack(CasinoTable table, string playerId, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, playerId);
        if (table.Game != "blackjack" || table.Blackjack == null)
            throw new InvalidOperationException("Wait until the current game finishes before leaving.");
        seat.LeaveAfterRound = true;
        var round = table.Blackjack;
        var hand = round.Hands.Single(hand => hand.Position == seat.Position);
        if (round.Hands[round.Turn].Position == seat.Position)
            Games.Blackjack.Act(round, seat.Position, "stand", clock(), options.TurnSeconds);
        else if (hand.Status == "playing")
            hand.Status = "stand";
        FinishBlackjack(table, changes);
    }
    private void FinishBlackjack(CasinoTable table, List<TableWalletChange> changes)
    {
        var round = table.Blackjack!;
        if (round.Phase != "complete" || table.Phase != "playing")
            return;
        table.Phase = "waiting";
        foreach (var seat in table.Seats)
        {
            var profit = round.Hands.Single(hand => hand.Position == seat.Position).Profit;
            seat.Stack = checked(seat.Stack + profit);
            changes.Add(new(seat.PlayerId, 0, profit, -seat.Liability));
            seat.Liability = 0;
        }
        foreach (var leaving in table.Seats.Where(seat => seat.LeaveAfterRound).Select(seat => seat.PlayerId).ToArray())
            RemoveSeat(table, leaving, changes);
    }
    private async Task AdvanceRoundsLocked()
    {
        await AdvancePokerLocked();
        await AdvanceRouletteLocked();
        await AdvanceEliminationLocked();
        var expired = state.Tables.Where(table => table.Phase == "playing" && table.Blackjack?.Phase == "playing" && table.Blackjack.Deadline <= clock()).Select(table => table.Id).ToArray();
        if (expired.Length == 0)
            return;
        var next = Clone(state);
        var changes = new List<TableWalletChange>();
        foreach (var id in expired)
        {
            var table = next.Tables.Single(table => table.Id == id);
            var round = table.Blackjack!;
            Games.Blackjack.Act(round, round.Hands[round.Turn].Position, "stand", clock(), options.TurnSeconds);
            FinishBlackjack(table, changes);
            table.Version = checked(table.Version + 1);
        }
        next.Revision = checked(next.Revision + 1);
        await CommitLocked(next, changes);
    }
}
