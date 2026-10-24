using RippersCasino.Core.Games;
namespace RippersCasino.Core.Tables;

public sealed partial class TableCoordinator
{
    private void StartRoulette(CasinoTable table, string id)
    {
        if (table.Game != "roulette" || table.Phase != "waiting" || table.OwnerId != id)
            throw new InvalidOperationException("Cash out the current round before opening betting.");
        table.Roulette = new()
        {
            Deadline = checked(clock() + options.RouletteBetSeconds)
        };
        table.Phase = "playing";
    }
    private void BetRoulette(CasinoTable table, string id, string selection, int amount, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, id);
        var r = table.Roulette;
        if (table.Game != "roulette" || table.Phase != "playing" || r?.Phase != "betting" || r.Deadline <= clock() || seat.LeaveAfterRound)
            throw new InvalidOperationException("Roulette betting is closed.");
        var oldLiability = seat.Liability;
        if (selection == "clear")
            r.Bets.RemoveAll(b => b.Position == seat.Position);
        else
        {
            _ = Games.Roulette.Rules(selection);
            if (amount < 1 || amount > options.MaxWager || r.Bets.Where(b => b.Position == seat.Position).Sum(b => b.Stake) > seat.Stack - amount)
                throw new InvalidOperationException("Not enough table chips for that roulette bet.");
            var existing = r.Bets.SingleOrDefault(b => b.Position == seat.Position && b.Selection == selection);
            if (existing == null)
                r.Bets.Add(new()
                {
                    Position = seat.Position,
                    Selection = selection,
                    Stake = amount
                });
            else
                existing.Stake = checked(existing.Stake + amount);
        }
        var liability = Games.Roulette.Liability(r, seat.Position);
        var account = wallets.Read(id);
        if (account.Chips > account.Limit - account.Held - liability)
            throw new InvalidOperationException("Withdraw some chips or choose a smaller bet.");
        seat.Liability = liability;
        changes.Add(new(id, 0, 0, liability - oldLiability));
    }
    private void LeaveRoulette(CasinoTable table, string id)
    {
        var seat = RequireSeat(table, id);
        seat.LeaveAfterRound = true;
        // Placed bets stay committed until the common deadline and result.
    }
    private static void FinishRoulette(CasinoTable table, List<TableWalletChange> changes)
    {
        var r = table.Roulette!;
        table.Phase = "waiting";
        foreach (var seat in table.Seats)
        {
            var profit = r.Profits.GetValueOrDefault(seat.Position);
            seat.Stack = checked(seat.Stack + profit);
            changes.Add(new(seat.PlayerId, 0, profit, -seat.Liability));
            seat.Liability = 0;
        }
        foreach (var leaving in table.Seats.Where(s => s.LeaveAfterRound).Select(s => s.PlayerId).ToArray())
            RemoveSeat(table, leaving, changes);
    }
    private async Task AdvanceRouletteLocked()
    {
        var expired = state.Tables.Where(t => t.Phase == "playing" && t.Roulette?.Phase == "betting" && t.Roulette.Deadline <= clock()).Select(t => t.Id).ToArray();
        if (expired.Length == 0)
            return;
        var next = Clone(state);
        var changes = new List<TableWalletChange>();
        foreach (var id in expired)
        {
            var table = next.Tables.Single(t => t.Id == id);
            var r = table.Roulette!;
            Games.Roulette.Resolve(r, random, clock());
            FinishRoulette(table, changes);
            table.Version++;
        }
        next.Revision++;
        await CommitLocked(next, changes);
    }
}
