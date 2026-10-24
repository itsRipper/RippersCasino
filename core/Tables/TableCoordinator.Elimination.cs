using RippersCasino.Core.Games;
namespace RippersCasino.Core.Tables;

public sealed partial class TableCoordinator
{
    private void StartElimination(CasinoTable table, string id, int stake, List<TableWalletChange> changes)
    {
        if (table.Capacity != 2)
            throw new InvalidOperationException("Russian Roulette is a two-person match.");
        if (table.Game != "russian-roulette" || table.Phase != "waiting" || table.OwnerId != id || stake < 1 || table.Seats.Any(s => s.Stack < stake))
            throw new InvalidOperationException("Choose a wager within your table bankroll.");
        var players = table.Seats.Select(s => new EliminationPlayer { Position = s.Position, Name = s.Name, Bankroll = s.Stack }).ToList();
        if (players.Count == 1)
        {
            ReservePotential(table, id, stake, false, changes);
            var old = table.Elimination?.Players.FirstOrDefault(p => p.Npc);
            players.Add(new EliminationPlayer
            {
                Position = 1 - players[0].Position,
                Name = old?.Name ?? Gamblers[random.Next(Gamblers.Length)],
                Npc = true,
                Bankroll = stake
            });
        }
        foreach (var s in table.Seats)
            ReservePotential(table, s.PlayerId, checked((players.Count - 1L) * stake), true, changes);
        table.Elimination = Games.RussianRoulette.Start(players, stake, random, clock(), options.TurnSeconds);
        table.Phase = "playing";
    }
    private void ActElimination(CasinoTable table, string id, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, id);
        if (table.Game != "russian-roulette" || table.Phase != "playing" || seat.LeaveAfterRound)
            throw new InvalidOperationException("There is no active match for you.");
        Games.RussianRoulette.TakeTurn(table.Elimination!, seat.Position, clock(), options.TurnSeconds);
        FinishElimination(table, changes);
    }
    private void LeaveElimination(CasinoTable table, string id, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, id);
        seat.LeaveAfterRound = true;
        Games.RussianRoulette.Forfeit(table.Elimination!, seat.Position, clock());
        FinishElimination(table, changes);
    }
    private void FinishElimination(CasinoTable table, List<TableWalletChange> changes)
    {
        if (table.Phase != "playing" || table.Elimination?.Phase != "complete")
            return;
        table.Phase = "waiting";
        foreach (var seat in table.Seats)
        {
            var profit = Games.RussianRoulette.Profit(table.Elimination, seat.Position);
            seat.Stack = checked(seat.Stack + profit);
            changes.Add(new(seat.PlayerId, 0, profit, -seat.Liability));
            seat.Liability = 0;
        }
        foreach (var leaving in table.Seats.Where(s => s.LeaveAfterRound).Select(s => s.PlayerId).ToArray())
            RemoveSeat(table, leaving, changes);
    }
    private async Task AdvanceEliminationLocked()
    {
        var expired = state.Tables.Where(t => t.Phase == "playing" && t.Elimination?.Phase == "playing" && t.Elimination.Deadline <= clock()).Select(t => t.Id).ToArray();
        if (expired.Length == 0)
            return;
        var next = Clone(state);
        var changes = new List<TableWalletChange>();
        foreach (var id in expired)
        {
            var table = next.Tables.Single(t => t.Id == id);
            var r = table.Elimination!;
            var p = r.Players.Single(p => p.Position == r.Turn);
            if (p.Npc)
                Games.RussianRoulette.TakeTurn(r, p.Position, clock(), options.TurnSeconds);
            else
                Games.RussianRoulette.Forfeit(r, p.Position, clock());
            FinishElimination(table, changes);
            table.Version++;
        }
        next.Revision++;
        await CommitLocked(next, changes);
    }
}
