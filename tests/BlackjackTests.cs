using RippersCasino.Core;
using RippersCasino.Client;
using RippersCasino.Core.Games;

internal static class BlackjackTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Func<bool> check) => test(name, () => { if (!check()) throw new Exception("Blackjack assertion failed"); });
        Check("blackjack rejects four hands before drawing randomness", () =>
        {
            var random = new CountingRandom();
            try { Blackjack.Deal(Enumerable.Range(0, 4).Select(position => new BlackjackHand { Position = position, Bankroll = 10, Bet = 2 }).ToList(), random, 6, 100, 45); return false; }
            catch (ArgumentException) { return random.Calls == 0; }
        });
        Check("blackjack values aces as eleven then one", () => Blackjack.Value([0, 13, 8]).Total == 21 && Blackjack.Value([0, 13, 9]).Total == 12 && !Blackjack.Value([0, 13, 9]).Soft && Blackjack.Value([0, 5]).Soft);
        Check("blackjack distinguishes natural from three-card twenty-one", () => Blackjack.Natural([0, 12]) && !Blackjack.Natural([0, 3, 5]));
        Check("blackjack settlement drives selection from 61 through a 60-chip loss", () =>
        {
            var bet = new BlackjackBetSelection(); bet.Refresh(61, 10000);
            for (var i = 0; i < 29; i++) bet.Step(1);
            var r = Round([9, 8], [9, 12]); r.Hands[0].Bankroll = 61; r.Hands[0].Bet = bet.Value;
            Blackjack.Act(r, 0, "stand", 100, 45);
            bet.Refresh(r.Hands[0].Bankroll + r.Hands[0].Profit, 10000);
            return r.Hands[0].Profit == -60 && bet.Value == 0 && bet.Maximum == 0;
        });
        Check("blackjack selection allows increments after winnings and always returns to two", () =>
        {
            var bet = new BlackjackBetSelection(); bet.Refresh(61, 10000);
            for (var i = 0; i < 29; i++) bet.Step(1);
            var r = Round([9, 12], [9, 8]); r.Hands[0].Bankroll = 61; r.Hands[0].Bet = bet.Value;
            Blackjack.Act(r, 0, "stand", 100, 45); bet.Refresh(61 + r.Hands[0].Profit, 10000);
            bet.Step(1); if (bet.Value != 62 || !bet.Valid) return false;
            bet.Step(1); if (bet.Value != 64 || !bet.Valid) return false;
            for (var i = 0; i < 100; i++) bet.Step(-1);
            return r.Hands[0].Profit == 60 && bet.Value == 2;
        });
        Check("six-deck shoe has exactly six copies of every card", () => Cards.Shuffle(new SecureRandom(), 6).GroupBy(card => card).All(group => group.Count() == 6));
        Check("blackjack dealer stands on soft seventeen", () => { var r = Round([9, 7], [0, 5]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Dealer.Count == 2 && r.Hands[0].Profit == 2; });
        Check("blackjack dealer draws below seventeen", () => { var r = Round([9, 7], [9, 4], [1]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Dealer.Count == 3 && r.Dealer.Sum(card => Math.Min(10, Cards.Rank(card))) == 17; });
        Check("blackjack natural pays three to two", () => { var r = Round([0, 12], [9, 6]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Hands[0].Profit == 3; });
        Check("blackjack equal naturals push", () => { var r = Round([0, 12], [13, 25]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Hands[0].Profit == 0; });
        Check("blackjack dealer natural beats ordinary twenty-one", () => { var r = Round([9, 4, 5], [0, 12]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Hands[0].Profit == -2; });
        Check("blackjack equal totals push", () => { var r = Round([9, 6], [22, 19]); Blackjack.Act(r, 0, "stand", 100, 45); return r.Hands[0].Status == "push"; });
        Check("blackjack bust loses even when dealer would bust", () => { var r = Round([9, 8], [9, 5], [4]); Blackjack.Act(r, 0, "hit", 100, 45); return r.Hands[0].Status == "bust" && r.Hands[0].Profit == -2; });
        Check("blackjack double takes one card and doubles the stake", () => { var r = Round([4, 5], [9, 6], [9]); Blackjack.Act(r, 0, "double", 100, 45); return r.Hands[0].Cards.Count == 3 && r.Hands[0].Bet == 4 && r.Hands[0].Profit == 4; });
        Check("blackjack double rejects insufficient table chips", () => { var r = Round([4, 5], [9, 6], [9]); r.Hands[0].Bankroll = 3; try { Blackjack.Act(r, 0, "double", 100, 45); return false; } catch (InvalidOperationException) { return r.Hands[0].Bet == 2; } });
        Check("blackjack NPC uses visible dealer card and its own hand", () => Blackjack.NpcDecision(new() { Cards = [9, 5], Bet = 2, Bankroll = 10 }, 4) == "stand" && Blackjack.NpcDecision(new() { Cards = [9, 5], Bet = 2, Bankroll = 10 }, 8) == "hit");
    }
    private static BlackjackRound Round(int[] player, int[] dealer, int[]? shoe = null) => new()
    {
        Shoe = shoe ?? [],
        Dealer = dealer.ToList(),
        Hands = [new() { Position = 0, Name = "Alice", Bet = 2, Bankroll = 10, Cards = player.ToList() }]
    };
    private sealed class CountingRandom : IRandomSource
    {
        internal int Calls;
        public int Next(int maximum) { Calls++; return maximum - 1; }
    }
}
