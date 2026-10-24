namespace RippersCasino.Core.Games;

public sealed class RouletteBet
{
    public int Position { get; set; }
    public string Selection { get; set; } = "";
    public long Stake { get; set; }
}
public sealed class RouletteRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "betting";
    public long Deadline { get; set; }
    public long ClosedAt { get; set; }
    public int Number { get; set; } = -1;
    public List<RouletteBet> Bets { get; set; } = [];
    public Dictionary<int, long> Profits { get; set; } = [];
}
public sealed record RouletteBetView(int Position, string Selection, long Stake);
public sealed record RouletteView(string Id, string Phase, long Deadline, long ClosedAt, int Number, RouletteBetView[] Bets, Dictionary<int, long> Profits);
public static class Roulette
{
    public static readonly int[] Wheel = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];
    public static bool Red(int n) => n is 1 or 3 or 5 or 7 or 9 or 12 or 14 or 16 or 18 or 19 or 21 or 23 or 25 or 27 or 30 or 32 or 34 or 36;
    public static (int[] Numbers, int Return) Rules(string selection)
    {
        if (selection is "red" or "black" or "odd" or "even" or "low" or "high")
            return (Enumerable.Range(1, 36).Where(n => selection switch
            {
                "red" => Red(n),
                "black" => !Red(n),
                "odd" => n % 2 == 1,
                "even" => n % 2 == 0,
                "low" => n <= 18,
                _ => n >= 19
            }).ToArray(), 2);
        var parts = selection.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var value) || parts[1] != value.ToString())
            throw new ArgumentException("Choose a bet on the roulette board.");
        if (parts[0] == "number" && value is >= 0 and <= 36)
            return ([value], 36);
        if (parts[0] == "dozen" && value is >= 1 and <= 3)
            return (Enumerable.Range((value - 1) * 12 + 1, 12).ToArray(), 3);
        if (parts[0] == "column" && value is >= 1 and <= 3)
            return (Enumerable.Range(1, 36).Where(n => (n - 1) % 3 + 1 == value).ToArray(), 3);
        throw new ArgumentException("Choose a bet on the roulette board.");
    }
    public static long Profit(RouletteRound round, int position, int number) => round.Bets.Where(b => b.Position == position)
        .Sum(b => checked(b.Stake * (Rules(b.Selection).Numbers.Contains(number) ? Rules(b.Selection).Return - 1 : -1)));
    public static long Liability(RouletteRound round, int position) => Enumerable.Range(0, 37).Max(n => Math.Max(0, Profit(round, position, n)));
    public static void Resolve(RouletteRound r, IRandomSource random, long now)
    {
        if (r.Phase != "betting" || now < r.Deadline)
            throw new InvalidOperationException("Roulette betting is still open.");
        r.Number = random.Next(37);
        r.ClosedAt = now;
        r.Phase = "complete";
        foreach (var position in r.Bets.Select(b => b.Position).Distinct())
            r.Profits[position] = Profit(r, position, r.Number);
    }
    public static RouletteView View(RouletteRound r) => new(r.Id, r.Phase, r.Deadline, r.ClosedAt, r.Number,
        r.Bets.Select(b => new RouletteBetView(b.Position, b.Selection, b.Stake)).ToArray(), new(r.Profits));
    public static void Validate(RouletteRound r)
    {
        if (!Guid.TryParseExact(r.Id, "N", out _) || r.Phase is not ("betting" or "complete") || r.Deadline <= 0 ||
            r.Bets.Count > 6 * 49 || r.Bets.Any(b => b.Position is < 0 or > 5 || b.Stake <= 0) ||
            r.Bets.GroupBy(b => (b.Position, b.Selection)).Any(g => g.Count() != 1) ||
            r.Phase == "betting" && (r.Number != -1 || r.Profits.Count != 0 || r.ClosedAt != 0) ||
            r.Phase == "complete" && (r.Number is < 0 or > 36 || r.ClosedAt < r.Deadline))
            throw new InvalidDataException("Your saved spin could not be restored. Close the casino and check the log.");
        foreach (var bet in r.Bets)
            _ = Rules(bet.Selection);
        if (r.Phase == "complete" && (r.Profits.Count != r.Bets.Select(b => b.Position).Distinct().Count() ||
            r.Profits.Any(p => p.Value != Profit(r, p.Key, r.Number))))
            throw new InvalidDataException("Your saved spin could not be restored. Close the casino and check the log.");
    }
}
