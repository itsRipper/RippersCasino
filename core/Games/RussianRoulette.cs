namespace RippersCasino.Core.Games;

public sealed class EliminationPlayer
{
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public bool Npc { get; set; }
    public long Bankroll { get; set; }
    public string Status { get; set; } = "in";
}
public sealed record EliminationEvent(int Sequence, int Position, string Result, long At, int Chamber);
public sealed class EliminationRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "playing";
    public int Stake { get; set; }
    public int Armed { get; set; }
    public int Cursor { get; set; }
    public int Turn { get; set; }
    public long Deadline { get; set; }
    public int Winner { get; set; } = -1;
    public List<EliminationPlayer> Players { get; set; } = [];
    public List<EliminationEvent> Events { get; set; } = [];
}
public sealed record EliminationPlayerView(int Position, string Name, bool Npc, string Status, long Profit);
public sealed record EliminationView(string Id, string Phase, int Stake, long Pool, int Turn, long Deadline, int Winner,
    int EmptyChambers, EliminationPlayerView[] Players, EliminationEvent[] Events);

// Fictional chip-only elimination game; no inventory, health or raid effects.
public static class RussianRoulette
{
    private static bool Alive(EliminationPlayer p) => p.Status == "in";
    private static void Deadline(EliminationRound r, long now, int seconds) => r.Deadline = checked(now + (r.Players.Single(p => p.Position == r.Turn).Npc ? 2 : seconds));
    public static EliminationRound Start(List<EliminationPlayer> players, int stake, IRandomSource random, long now, int seconds)
    {
        if (players.Count != 2 || players.Select(p => p.Position).Distinct().Count() != 2 || players.Any(p => p.Position is < 0 or > 1) || stake < 1 || players.Any(p => p.Bankroll < stake))
            throw new InvalidOperationException("Both opponents must match the wager.");
        var r = new EliminationRound { Players = players, Stake = stake, Armed = random.Next(6), Turn = players[random.Next(players.Count)].Position };
        Deadline(r, now, seconds);
        return r;
    }
    public static void TakeTurn(EliminationRound r, int position, long now, int seconds)
    {
        if (r.Phase != "playing" || r.Turn != position)
            throw new InvalidOperationException("Wait for your turn.");
        var p = r.Players.Single(p => p.Position == position);
        var chamber = r.Cursor;
        var eliminated = r.Cursor == r.Armed;
        if (eliminated)
            p.Status = "out";
        else
            r.Cursor++;
        r.Events.Add(new(r.Events.Count + 1, position, eliminated ? "eliminated" : "safe", now, chamber));
        if (eliminated)
        {
            Finish(r);
            return;
        }
        r.Turn = 1 - position;
        Deadline(r, now, seconds);
    }
    public static void Forfeit(EliminationRound r, int position, long now)
    {
        if (r.Phase != "playing")
            throw new InvalidOperationException("This match has finished.");
        var p = r.Players.Single(p => p.Position == position);
        if (!Alive(p))
            return;
        p.Status = "forfeit";
        r.Events.Add(new(r.Events.Count + 1, position, "forfeit", now, r.Cursor));
        Finish(r);
    }
    private static void Finish(EliminationRound r)
    {
        var survivor = r.Players.Single(Alive);
        r.Winner = survivor.Position;
        survivor.Status = "winner";
        r.Phase = "complete";
        r.Deadline = 0;
    }
    public static long Profit(EliminationRound r, int position) => r.Phase != "complete" ? 0 : position == r.Winner ? checked((r.Players.Count - 1L) * r.Stake) : -r.Stake;
    public static EliminationView View(EliminationRound r) => new(r.Id, r.Phase, r.Stake, checked(r.Stake * (long)r.Players.Count), r.Turn, r.Deadline, r.Winner, r.Cursor,
        r.Players.Select(p => new EliminationPlayerView(p.Position, p.Name, p.Npc, p.Status, Profit(r, p.Position))).ToArray(), r.Events.ToArray());
    public static void Validate(EliminationRound r)
    {
        if (!Guid.TryParseExact(r.Id, "N", out _) || r.Phase is not ("playing" or "complete") || r.Stake < 1 || r.Armed is < 0 or > 5 || r.Cursor is < 0 or > 5 ||
            r.Players.Count != 2 || r.Players.Select(p => p.Position).Distinct().Count() != r.Players.Count ||
            r.Players.Any(p => p.Position < 0 || p.Position > 1 || p.Bankroll < r.Stake || p.Status is not ("in" or "out" or "forfeit" or "winner")) ||
            r.Events.Count > 36 || r.Events.Where((e, i) => e.Sequence != i + 1 || e.Result is not ("safe" or "eliminated" or "forfeit") || e.Chamber is < 0 or > 5 || e.At <= 0 || !r.Players.Any(p => p.Position == e.Position)).Any() ||
            r.Phase == "playing" && (r.Winner != -1 || r.Deadline <= 0 || r.Cursor > r.Armed || r.Players.Count(Alive) < 2 || !r.Players.Any(p => p.Position == r.Turn && Alive(p))) ||
            r.Phase == "complete" && (r.Deadline != 0 || r.Players.Count(p => p.Status == "winner") != 1 || r.Players.Any(Alive) || !r.Players.Any(p => p.Position == r.Winner && p.Status == "winner")))
            throw new InvalidDataException("Your saved match could not be restored. Close the casino and check the log.");
        if (r.Players.Sum(p => Profit(r, p.Position)) != 0)
            throw new InvalidDataException("The prize could not be paid. Close the casino and check the log.");
    }
}
