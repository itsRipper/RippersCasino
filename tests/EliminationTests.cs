using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Core.Games;
internal static class EliminationTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Action action) => test("fictional elimination " + name, action);
        Check("uses one hidden losing chamber and continuous rotation", () =>
        {
            var rng = new Last(); var r = Start(2, rng);
            Require(r.Armed == 5 && r.Cursor == 0 && rng.Calls == 2);
            for (var i = 0; i < 5; i++) { RussianRoulette.TakeTurn(r, r.Turn, 101 + i, 45); Require(r.Phase == "playing" && r.Cursor == i + 1 && rng.Calls == 2); }
            RussianRoulette.TakeTurn(r, r.Turn, 110, 45); Require(r.Phase == "complete" && r.Events.Last().Result == "eliminated" && rng.Calls == 2);
        });
        Check("rejects a third participant before consuming randomness", () =>
        {
            var rng = new Last(); Fails(() => Start(3, rng)); Require(rng.Calls == 0);
        });
        Check("rejects unsupported saved multi-seat rounds", () =>
        {
            var r = new EliminationRound { Players = Players(3), Stake = 2, Armed = 5, Turn = 2, Deadline = 145 };
            var before = JsonSerializer.Serialize(r);
            try { RussianRoulette.Validate(r); throw new Exception("Expected saved-data rejection"); } catch (InvalidDataException) { }
            Require(JsonSerializer.Serialize(r) == before);
        });
        Check("public view excludes armed chamber and bankroll internals", () =>
        {
            var r = Start(2, new Last()); var wire = JsonSerializer.Serialize(RussianRoulette.View(r));
            Require(!wire.Contains("Armed") && !wire.Contains("Cursor") && !wire.Contains("Bankroll") && wire.Contains("EmptyChambers"));
        });
        Check("cannot act out of turn or after completion", () =>
        {
            var r = Start(2, new Last()); Fails(() => RussianRoulette.TakeTurn(r, 0, 100, 45));
            RussianRoulette.Forfeit(r, r.Turn, 100); Fails(() => RussianRoulette.TakeTurn(r, r.Turn, 100, 45));
        });
        Check("out-of-turn forfeit ends the confrontation with one winner", () =>
        {
            var r = Start(2, new Last()); RussianRoulette.Forfeit(r, 0, 130);
            Require(r.Phase == "complete" && r.Winner == 1 && r.Players[0].Status == "forfeit");
        });
        Check("winner receives entry pool and all participants balance", () =>
        {
            var r = Start(2, new Last()); RussianRoulette.Forfeit(r, 0, 101);
            Require(RussianRoulette.Profit(r, 1) == 2 && RussianRoulette.Profit(r, 0) == -2 && r.Players.Sum(p => RussianRoulette.Profit(r, p.Position)) == 0);
            RussianRoulette.Validate(r);
        });
        Check("NPC turns have two-second pacing and humans retain full deadline", () =>
        {
            var rng = new Last(); var players = Players(2); players[1].Npc = true;
            var r = RussianRoulette.Start(players, 2, rng, 100, 45); Require(r.Deadline == 102);
            RussianRoulette.TakeTurn(r, 1, 102, 45); Require(r.Turn == 0 && r.Deadline == 147);
        });
        Check("one thousand 1v1 matches terminate within six turns with one winner", () =>
        {
            var rng = new SecureRandom();
            for (var sample = 0; sample < 1000; sample++)
            {
                var r = Start(2, rng);
                for (var i = 0; r.Phase == "playing" && i < 6; i++)
                {
                    RussianRoulette.TakeTurn(r, r.Turn, 101 + i, 45); RussianRoulette.Validate(r);
                }
                Require(r.Phase == "complete" && r.Players.Count(p => p.Status == "winner") == 1 && r.Players.Sum(p => RussianRoulette.Profit(r, p.Position)) == 0);
            }
        });
    }
    private static List<EliminationPlayer> Players(int count) => Enumerable.Range(0, count).Select(i => new EliminationPlayer { Position = i, Name = "P" + i, Bankroll = 10 }).ToList();
    private static EliminationRound Start(int count, IRandomSource rng) => RussianRoulette.Start(Players(count), 2, rng, 100, 45);
    private static void Require(bool value) { if (!value) throw new Exception("Fictional elimination assertion failed"); }
    private static void Fails(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    private sealed class Last : IRandomSource { public int Calls; public int Next(int max) { Calls++; return max - 1; } }
}
