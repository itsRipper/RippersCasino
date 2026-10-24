using RippersCasino.Core;
using RippersCasino.Core.Games;
internal static class RouletteTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Action action) => test("roulette " + name, action);
        Check("wheel contains one of every number and eighteen red pockets", () => Require(Roulette.Wheel.Order().SequenceEqual(Enumerable.Range(0, 37)) && Enumerable.Range(0, 37).Count(Roulette.Red) == 18));
        Check("all supported fields have correct coverage and gross return", () =>
        {
            var keys = Enumerable.Range(0, 37).Select(n => "number:" + n).Concat(new[] { "red", "black", "odd", "even", "low", "high" })
                .Concat(Enumerable.Range(1, 3).Select(n => "dozen:" + n)).Concat(Enumerable.Range(1, 3).Select(n => "column:" + n));
            foreach (var key in keys)
            {
                var rule = Roulette.Rules(key); Require(rule.Numbers.Length * rule.Return == 36 && rule.Numbers.Distinct().Count() == rule.Numbers.Length);
                var r = new RouletteRound { Bets = [new() { Selection = key, Position = 0, Stake = 3 }] };
                foreach (var number in Enumerable.Range(0, 37)) Require(Roulette.Profit(r, 0, number) == (rule.Numbers.Contains(number) ? 3L * (rule.Return - 1) : -3));
            }
        });
        Check("zero loses every outside bet", () =>
        {
            foreach (var key in new[] { "red", "black", "odd", "even", "low", "high", "dozen:1", "column:1" }) Require(!Roulette.Rules(key).Numbers.Contains(0));
        });
        Check("invalid and ambiguous selections reject", () =>
        {
            foreach (var key in new[] { "number:37", "number:-1", "number:00", "number:01", "dozen:0", "column:4", "split:1", "red:1", "" })
            { try { Roulette.Rules(key); throw new Exception("Selection accepted"); } catch (ArgumentException) { } }
        });
        Check("payout capacity uses actual maximum across combined bets", () =>
        {
            var r = new RouletteRound { Bets = [new() { Position = 0, Selection = "red", Stake = 5 }, new() { Position = 0, Selection = "black", Stake = 5 }] };
            Require(Roulette.Liability(r, 0) == 0); r.Bets.Add(new() { Position = 0, Selection = "number:0", Stake = 1 }); Require(Roulette.Liability(r, 0) == 25);
        });
        Check("result cannot resolve before common deadline", () =>
        {
            var r = new RouletteRound { Deadline = 120 }; try { Roulette.Resolve(r, new SecureRandom(), 119); throw new Exception("Early spin"); } catch (InvalidOperationException) { }
            Require(r.Number == -1 && r.Phase == "betting"); Roulette.Resolve(r, new SecureRandom(), 120); Roulette.Validate(r);
        });
    }
    private static void Require(bool value) { if (!value) throw new Exception("Roulette assertion failed"); }
}
