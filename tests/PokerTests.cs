using RippersCasino.Core;
using RippersCasino.Core.Games;
internal static class PokerTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Action check) => test("poker " + name, check);
        Check("licensed evaluator orders all hand categories", () =>
        {
            var hands = new[] { "As Ks Qs Js Ts", "9s 8s 7s 6s 5s", "As Ac Ad Ah 2s", "As Ac Ad Ks Kc", "As Js 8s 4s 2s", "As 2c 3d 4h 5s", "As Ac Ad 9h 2s", "As Ac Kd Kh 2s", "As Ac Qd 9h 2s", "As Kc Jd 9h 2s" };
            var ranks = hands.Select(h => Poker.Rank(Parse(h))).ToArray();
            Require(ranks.Zip(ranks.Skip(1)).All(pair => pair.First < pair.Second));
        });
        Check("seven-card rank selects best five and wheel loses to six-high", () =>
        {
            Require(Poker.Rank(Parse("As Ks Qs Js Ts 2h 3d")) == Poker.Rank(Parse("As Ks Qs Js Ts")));
            Require(Poker.Rank(Parse("As 2c 3d 4h 5s")) > Poker.Rank(Parse("2s 3c 4d 5h 6s")));
        });
        Check("heads-up button posts small blind and acts first", () =>
        {
            var r = Deal(100, 100); Require(r.Turn == 0 && r.Players[0].StreetBet == 1 && r.Players[1].StreetBet == 2);
            Act(r, "call"); Act(r, "check"); Require(r.Street == "flop" && r.Turn == 1 && r.Board.Count == 3);
        });
        Check("private view excludes opponent holes and deck", () =>
        {
            var r = Deal(100, 100); var v = Poker.View(r, 0);
            Require(v.Players[0].Cards.SequenceEqual(r.Players[0].Hole) && v.Players[1].Cards.SequenceEqual(new[] { -1, -1 }));
            var wire = System.Text.Json.JsonSerializer.Serialize(v); Require(!wire.Contains("Deck") && !wire.Contains("Drawn"));
        });
        Check("check and undersized raises reject without moving chips", () =>
        {
            var r = Deal(100, 100); Fails(() => Act(r, "check")); Fails(() => Act(r, "raise", 3));
            Require(r.Players[0].Stack == 99 && r.Players[0].StreetBet == 1);
        });
        Check("short all-in does not reopen prior raiser", () =>
        {
            var r = Deal(100, 100, 5); Act(r, "raise", 4); Act(r, "call"); Act(r, "all-in");
            Require(r.CurrentBet == 5 && r.LastRaise == 2 && r.Turn == 0 && !Poker.MayRaise(r, r.Players[0]));
            Fails(() => Act(r, "raise", 7)); Act(r, "call"); Act(r, "call"); Require(r.Street == "flop");
        });
        Check("cumulative short all-ins reopen a prior full raiser", () =>
        {
            var r = Deal(100, 5, 6, 100); Act(r, "raise", 4); Act(r, "call"); Act(r, "all-in"); Act(r, "all-in");
            Require(r.Turn == 3 && r.CurrentBet == 6 && r.LastRaise == 2 && Poker.MayRaise(r, r.Players[3]));
        });
        Check("full raise reopens betting", () =>
        {
            var r = Deal(100, 100, 100); Act(r, "raise", 4); Act(r, "raise", 8); Act(r, "call");
            Require(Poker.MayRaise(r, r.Players[0]) && Poker.MinimumRaise(r) == 12);
        });
        Check("all-ins run out all streets and conserve bankrolls", () =>
        {
            var r = Deal(10, 7); Act(r, "all-in"); Act(r, "call");
            Require(r.Phase == "complete" && r.Board.Count == 5 && r.Burned.Count == 3 && r.Players.Sum(p => p.Stack) == 17);
            Poker.Validate(r);
        });
        Check("side pots exclude folded money from eligibility", () =>
        {
            var r = Pot([10, 20, 30], [10, 20, 30], ["As Ah", "Ks Kh", "Qs Qh"], "2c 3d 7h 8s 9c");
            Poker.Settle(r); Require(r.Players.Select(p => p.Stack).SequenceEqual(new long[] { 30, 20, 10 }));
        });
        Check("folded contribution stays in pot and cannot win", () =>
        {
            var r = Pot([10, 10, 10], [10, 10, 10], ["As Ah", "Ks Kh", "Qs Qh"], "2c 3d 7h 8s 9c");
            r.Players[0].Folded = true; Poker.Settle(r); Require(r.Players[1].Stack == 30 && r.Players[0].Stack == 0);
        });
        Check("odd split chip goes left of button", () =>
        {
            var r = Pot([5, 5, 5], [5, 5, 5], ["2c 3c", "4c 5c", "6c 7c"], "As Ks Qs Js Ts");
            r.Players[2].Folded = true; Poker.Settle(r); Require(r.Players[1].Stack == 8 && r.Players[0].Stack == 7);
        });
        Check("uncontested overbet returns unmatched chips", () =>
        {
            var r = Deal(100, 10); Act(r, "all-in"); Act(r, "fold");
            Require(r.Phase == "complete" && r.Players[0].Stack == 102 && r.Players[1].Stack == 8 && !r.Showdown);
            Require(Poker.View(r, 1).Players[0].Cards.All(c => c == -1)); Poker.Validate(r);
        });
        Check("one thousand legal hands finish with exact chip conservation", () =>
        {
            var rng = new SecureRandom();
            for (var sample = 0; sample < 1000; sample++)
            {
                var players = Enumerable.Range(0, 2 + rng.Next(5)).Select(i => new PokerPlayer { Position = i, Name = "P" + i, Bankroll = 2 + rng.Next(99) }).ToList();
                var r = Poker.Deal(players, rng, 2, sample % players.Count, 100, 45);
                for (var turn = 0; r.Phase != "complete" && turn < 1000; turn++)
                {
                    Poker.Validate(r); var p = r.Players.Single(p => p.Position == r.Turn);
                    var call = r.CurrentBet > p.StreetBet; var roll = rng.Next(10);
                    if (roll == 0) Poker.Act(r, p.Position, "fold", 0, 100, 45, rng);
                    else if (roll == 1 && Poker.MayRaise(r, p)) Poker.Act(r, p.Position, "all-in", 0, 100, 45, rng);
                    else Poker.Act(r, p.Position, call ? "call" : "check", 0, 100, 45, rng);
                }
                Require(r.Phase == "complete" && r.Players.Sum(p => p.Stack) == r.Players.Sum(p => p.Bankroll)); Poker.Validate(r);
            }
        });
    }
    public static int[] Parse(string text) => text.Split(' ').Select(s => "cdhs".IndexOf(s[1]) * 13 + "A23456789TJQK".IndexOf(s[0])).ToArray();
    private static PokerRound Deal(params long[] stacks) => Poker.Deal(stacks.Select((s, i) => new PokerPlayer { Position = i, Name = "P" + i, Bankroll = s }).ToList(), new SecureRandom(), 2, 0, 100, 45);
    private static void Act(PokerRound r, string action, long amount = 0) => Poker.Act(r, r.Turn, action, amount, 100, 45, new SecureRandom());
    private static PokerRound Pot(long[] bankroll, long[] committed, string[] hole, string board) => new()
    {
        Board = Parse(board).ToList(), Showdown = true,
        Players = bankroll.Select((s, i) => new PokerPlayer { Position = i, Bankroll = s, Stack = s - committed[i], TotalBet = committed[i], Hole = Parse(hole[i]) }).ToList()
    };
    private static void Require(bool value) { if (!value) throw new Exception("Poker assertion failed"); }
    private static void Fails(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
}
