using RippersCasino.Core.Games;
namespace RippersCasino.Core.Tables;

public sealed partial class TableCoordinator
{
    private static readonly string[] PokerStyles = ["aggressive", "tight", "loose", "conservative", "opportunistic"];
    private void StartPoker(CasinoTable table, string playerId, int blind, List<TableWalletChange> changes)
    {
        if (table.Game != "poker" || table.Phase != "waiting" || table.OwnerId != playerId ||
            blind < 2 || blind % 2 != 0 || blind > options.MaxWager || table.Seats.Any(s => s.Stack < blind))
            throw new InvalidOperationException("Choose an even big blind within your table bankroll.");
        var players = table.Seats.Select(s => new PokerPlayer { Position = s.Position, Name = s.Name, Bankroll = s.Stack }).ToList();
        if (players.Count == 1)
        {
            var old = table.Poker?.Players.Where(p => p.Npc).ToArray();
            if (old?.Length > 0)
                players.AddRange(old.Select(p => new PokerPlayer { Position = p.Position, Name = p.Name, Npc = true, Style = p.Style, Bankroll = Math.Max(blind * 10L, p.Stack) }));
            else
            {
                // Preflight the largest possible solo field before consuming randomness.
                var maxNpc = Math.Min(4, table.Capacity - 1);
                ReservePotential(table, playerId, checked(players[0].Bankroll * maxNpc), false, changes);
                var count = random.Next(maxNpc) + 1;
                var names = Gamblers.OrderBy(_ => random.Next(int.MaxValue)).Take(count).ToArray();
                for (var i = 0; i < count; i++)
                    players.Add(new()
                    {
                        Position = i + 1,
                        Name = names[i],
                        Style = PokerStyles[i],
                        Npc = true,
                        Bankroll = table.Seats[0].Stack
                    });
            }
        }
        foreach (var seat in table.Seats)
            ReservePotential(table, seat.PlayerId, players.Where(p => p.Position != seat.Position).Sum(p => p.Bankroll), true, changes);
        var oldButton = table.Poker?.Button ?? -1;
        var button = players.OrderBy(p => (p.Position - oldButton + 6) % 6 == 0 ? 6 : (p.Position - oldButton + 6) % 6).First().Position;
        table.Phase = "playing";
        table.Poker = Games.Poker.Deal(players, random, blind, button, clock(), options.TurnSeconds);
        FinishPoker(table, changes);
    }
    private void ReservePotential(CasinoTable table, string id, long liability, bool reserve, List<TableWalletChange> changes)
    {
        var account = wallets.Read(id);
        if (!account.Available || account.Chips > account.Limit - account.Held - liability)
            throw new InvalidOperationException("Withdraw some chips or use a smaller table bankroll.");
        if (!reserve)
            return;
        var seat = RequireSeat(table, id);
        seat.Liability = liability;
        changes.Add(new(id, 0, 0, liability));
    }
    private void ActPoker(CasinoTable table, string id, string action, int amount, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, id);
        if (table.Game != "poker" || table.Phase != "playing" || table.Poker == null || seat.LeaveAfterRound)
            throw new InvalidOperationException("You have no active Poker hand.");
        Games.Poker.Act(table.Poker, seat.Position, action, amount, clock(), options.TurnSeconds, random);
        FinishPoker(table, changes);
    }
    private void LeavePoker(CasinoTable table, string id, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, id);
        seat.LeaveAfterRound = true;
        var round = table.Poker!;
        var p = round.Players.Single(p => p.Position == seat.Position);
        p.Leave = true;
        // An all-in remains eligible. Out-of-turn departures fold on their next legal turn.
        if (round.Turn == p.Position && !p.Folded && p.Stack > 0)
            Games.Poker.Act(round, p.Position, "fold", 0, clock(), options.TurnSeconds, random);
        FinishPoker(table, changes);
    }
    private void FinishPoker(CasinoTable table, List<TableWalletChange> changes)
    {
        if (table.Phase != "playing" || table.Poker?.Phase != "complete")
            return;
        table.Phase = "waiting";
        foreach (var seat in table.Seats)
        {
            var final = table.Poker.Players.Single(p => p.Position == seat.Position).Stack;
            changes.Add(new(seat.PlayerId, 0, final - seat.Stack, -seat.Liability));
            seat.Stack = final;
            seat.Liability = 0;
        }
        foreach (var id in table.Seats.Where(s => s.LeaveAfterRound).Select(s => s.PlayerId).ToArray())
            RemoveSeat(table, id, changes);
    }
    private async Task AdvancePokerLocked()
    {
        var expired = state.Tables.Where(t => t.Phase == "playing" && t.Poker?.Phase == "playing" && t.Poker.Deadline <= clock()).Select(t => t.Id).ToArray();
        if (expired.Length == 0)
            return;
        var next = Clone(state);
        var changes = new List<TableWalletChange>();
        foreach (var id in expired)
        {
            var table = next.Tables.Single(t => t.Id == id);
            var r = table.Poker!;
            var p = r.Players.Single(p => p.Position == r.Turn);
            Games.Poker.Act(r, p.Position, p.StreetBet >= r.CurrentBet ? "check" : "fold", 0, clock(), options.TurnSeconds, random);
            FinishPoker(table, changes);
            table.Version++;
        }
        next.Revision++;
        await CommitLocked(next, changes);
    }
}
