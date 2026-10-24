using RippersCasino.Core.Games;
using System.Text.Json;

namespace RippersCasino.Core.Tables;

public sealed partial class TableCoordinator
{
    public const int RecentActionLimit = 128;
    private static readonly string[] SupportedGames = ["blackjack", "poker", "russian-roulette", "roulette"];
    private readonly ITableWallets wallets;
    private readonly TableStorage storage;
    private readonly TableOptions options;
    private readonly Func<long> clock;
    private readonly IRandomSource random;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TableDatabase state;
    private TableJournal? pending;
    public TableCoordinator(ITableWallets wallets, TableStorage storage, TableOptions options, Func<long>? clock = null, IRandomSource? random = null)
    {
        options.Validate();
        this.wallets = wallets;
        this.storage = storage;
        this.options = options;
        this.random = random ?? new SecureRandom();
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        state = storage.Load();
        Validate(state);
        pending = storage.Pending();
        if (pending != null)
        {
            ValidateJournal(pending);
            ValidateRecovery(pending);
        }
    }
    public bool RecoveryPending => Volatile.Read(ref pending) != null;
    public bool Blocks(string playerId) => Volatile.Read(ref pending)?.Wallets.Any(change => change.PlayerId == playerId) == true;
    public async Task RecoverAsync()
    {
        await gate.WaitAsync();
        try
        {
            await RecoverLocked();
        }
        finally { gate.Release(); }
    }
    private async Task RecoverLocked()
    {
        if (pending != null)
        {
            var journal = pending;
            await using var lease = await wallets.LockAsync(journal.Wallets.Select(change => change.PlayerId));
            await FinishLocked(journal);
        }
    }

    private async Task FinishLocked(TableJournal journal)
    {
        foreach (var change in journal.Wallets)
            await wallets.ApplyAsync(change, journal.CommitId);
        storage.Commit(journal.State);
        storage.Complete();
        state = journal.State;
        Volatile.Write(ref pending, null);
    }
    public async Task<TableSnapshot> PollAsync(string playerId)
    {
        await gate.WaitAsync();
        try
        {
            await RecoverLocked();
            await AdvanceRoundsLocked();
            await using var lease = await wallets.LockAsync([playerId]);
            var account = wallets.Read(playerId);
            ValidateReservation(playerId, account);
            return View(playerId, account);
        }
        finally { gate.Release(); }
    }
    public async Task<TableSnapshot> ActAsync(string playerId, TableIntent intent)
    {
        IAsyncDisposable? roundLease = null;
        await gate.WaitAsync();
        try
        {
            await RecoverLocked();
            await AdvanceRoundsLocked();
            if (!Guid.TryParseExact(intent.RequestId, "N", out _) || intent.Version < 0)
                throw new ArgumentException("This action could not be submitted. Close and reopen the casino.");
            if (intent.Selection == null || intent.Selection.Length > 32)
                throw new ArgumentException("Choose a bet on the board.");
            var fingerprint = JsonSerializer.Serialize(intent);
            var receipt = state.Actions.FirstOrDefault(action => action.PlayerId == playerId && action.RequestId == intent.RequestId)
                ?? storage.Receipt(playerId, intent.RequestId);
            await using (var lease = await wallets.LockAsync([playerId]))
            {
                var account = wallets.Read(playerId);
                ValidateReservation(playerId, account);
                if (receipt != null)
                {
                    if (receipt.Fingerprint != fingerprint)
                        throw new ArgumentException("This action was already submitted. Close and reopen the casino.");
                    return View(playerId, account, true);
                }
                if (!account.Available && intent.Operation != "leave")
                    throw new InvalidOperationException("Requires Generator level 1 and Intelligence Center level 1.");
            }
            if (!options.Enabled && intent.Operation == "solo")
                throw new InvalidOperationException("Table games are unavailable.");
            var next = Clone(state);
            var changes = new List<TableWalletChange>();
            CasinoTable table;
            if (intent.Operation == "solo")
            {
                if (!SupportedGames.Contains(intent.Game) || intent.Amount < (intent.Game == "blackjack" || intent.Game == "poker" ? 2 : 1) || intent.Amount > options.MaxBuyIn)
                    throw new ArgumentException("Choose a game and a bankroll within the table limit.");
                RequireUnseated(next, playerId);
                table = new CasinoTable { Id = Guid.NewGuid().ToString("N"), OwnerId = playerId, Game = intent.Game, BuyIn = intent.Amount, Capacity = Capacity(intent.Game), Solo = true };
                next.Tables.Add(table);
                await AddSeat(table, playerId, changes);
            }
            else
            {
                table = next.Tables.SingleOrDefault(table => table.Id == intent.TableId)
                    ?? throw new ArgumentException("This game is no longer available.");
                if (table.Version != intent.Version)
                    throw new InvalidOperationException("The game has moved on. Wait for the table to update.");
                if (table.Phase == "closed" || table.Phase == "playing" && intent.Operation is not ("hit" or "stand" or "double" or "fold" or "check" or "call" or "raise" or "all-in" or "bet" or "take-turn" or "leave"))
                    throw new InvalidOperationException("Wait until the current round finishes.");
                switch (intent.Operation)
                {
                    case "leave":
                        if (table.Phase == "playing")
                        {
                            if (table.Game == "russian-roulette")
                                LeaveElimination(table, playerId, changes);
                            else if (table.Game == "roulette")
                                LeaveRoulette(table, playerId);
                            else if (table.Game == "poker")
                                LeavePoker(table, playerId, changes);
                            else
                                LeaveBlackjack(table, playerId, changes);
                        }
                        else
                            RemoveSeat(table, playerId, changes);
                        break;
                    case "start":
                        roundLease = await wallets.LockAsync(table.Seats.Select(seat => seat.PlayerId));
                        if (table.Game == "russian-roulette")
                            StartElimination(table, playerId, intent.Amount, changes);
                        else if (table.Game == "roulette")
                            StartRoulette(table, playerId);
                        else if (table.Game == "poker")
                            StartPoker(table, playerId, intent.Amount, changes);
                        else
                            StartBlackjack(table, playerId, intent.Amount, changes);
                        break;
                    case "take-turn":
                        ActElimination(table, playerId, changes);
                        break;
                    case "bet":
                        roundLease = await wallets.LockAsync([playerId]);
                        BetRoulette(table, playerId, intent.Selection, intent.Amount, changes);
                        break;
                    case "fold":
                    case "check":
                    case "call":
                    case "raise":
                    case "all-in":
                        ActPoker(table, playerId, intent.Operation, intent.Amount, changes);
                        break;
                    case "hit":
                    case "stand":
                    case "double":
                        ActBlackjack(table, playerId, intent.Operation, changes);
                        break;
                    default:
                        throw new ArgumentException("Unknown table action.");
                }
                table.Version = checked(table.Version + 1);
            }
            next.Revision = checked(next.Revision + 1);
            next.Actions.Add(new(playerId, intent.RequestId, fingerprint, table.Id));
            await CommitLocked(next, changes, roundLease != null);
            if (roundLease != null)
            {
                await roundLease.DisposeAsync();
                roundLease = null;
            }
            await using var readLease = await wallets.LockAsync([playerId]);
            return View(playerId, wallets.Read(playerId));
        }
        finally { if (roundLease != null) await roundLease.DisposeAsync(); gate.Release(); }
    }
    private async Task AddSeat(CasinoTable table, string playerId, List<TableWalletChange> changes)
    {
        await using var lease = await wallets.LockAsync([playerId]);
        var account = wallets.Read(playerId);
        if (account.Chips < table.BuyIn)
            throw new InvalidOperationException("Not enough available chips for this bankroll.");
        var position = Enumerable.Range(0, table.Capacity).First(index => table.Seats.All(seat => seat.Position != index));
        var name = new string(account.Name.Where(character => !char.IsControl(character)).Take(32).ToArray());
        table.Blackjack = null;
        table.Poker = null;
        table.Roulette = null;
        table.Elimination = null;
        table.Seats.Add(new()
        {
            PlayerId = playerId,
            Name = string.IsNullOrWhiteSpace(name) ? "Casino guest" : name,
            Position = position,
            Stack = table.BuyIn
        });
        changes.Add(new(playerId, -table.BuyIn, table.BuyIn));
    }
    private void ValidateReservation(string playerId, TableAccount account)
    {
        var reserved = state.Tables.SelectMany(table => table.Seats).Where(seat => seat.PlayerId == playerId).Sum(seat => seat.Stack);
        var liability = state.Tables.SelectMany(table => table.Seats).Where(seat => seat.PlayerId == playerId).Sum(seat => seat.Liability);
        if (account.Held != reserved || account.Liability != liability)
            throw new InvalidOperationException("Saved bankroll does not match table escrow or liability; records preserved.");
    }
    private static void RequireUnseated(TableDatabase database, string playerId)
    {
        if (database.Tables.Any(table => table.Seats.Any(seat => seat.PlayerId == playerId)))
            throw new InvalidOperationException("Cash out your current bankroll before opening another game.");
    }
    private static TableSeat RequireSeat(CasinoTable table, string playerId) => table.Seats.SingleOrDefault(seat => seat.PlayerId == playerId)
        ?? throw new InvalidOperationException("You are not seated at this table.");
    private static void RemoveSeat(CasinoTable table, string playerId, List<TableWalletChange> changes)
    {
        var seat = RequireSeat(table, playerId);
        changes.Add(new(playerId, seat.Stack, -seat.Stack));
        table.Seats.Remove(seat);
        if (table.Seats.Count == 0)
            table.Phase = "closed";
    }
    private async Task CommitLocked(TableDatabase next, List<TableWalletChange> changes, bool alreadyLocked = false)
    {
        changes = changes.GroupBy(change => change.PlayerId).Select(group => new TableWalletChange(group.Key,
            group.Sum(change => change.Chips), group.Sum(change => change.Held), group.Sum(change => change.Liability)))
            .Where(change => change.Chips != 0 || change.Held != 0 || change.Liability != 0).ToList();
        // Archive only receipts from the committed state, never the action about to commit.
        var retained = next.Actions.TakeLast(RecentActionLimit).ToArray();
        var retainedIds = retained.Select(action => (action.PlayerId, action.RequestId)).ToHashSet();
        foreach (var action in next.Actions.Where(action => !retainedIds.Contains((action.PlayerId, action.RequestId))))
        {
            if (!state.Actions.Contains(action)) throw new InvalidDataException("Cannot archive an uncommitted action.");
            storage.Archive(action);
        }
        next.Actions = retained.ToList();
        next.Tables.RemoveAll(table => table.Phase == "closed");
        var journal = new TableJournal(Guid.NewGuid().ToString("N"), next, changes.ToArray());
        ValidateJournal(journal);
        ValidateRecovery(journal);
        await using var lease = alreadyLocked ? null : await wallets.LockAsync(changes.Select(change => change.PlayerId));
        foreach (var change in changes)
        {
            var account = wallets.Read(change.PlayerId);
            var chips = checked(account.Chips + change.Chips);
            var held = checked(account.Held + change.Held);
            var liability = checked(account.Liability + change.Liability);
            if (chips < 0 || held < 0 || liability < 0 || chips > account.Limit - held - liability)
                throw new InvalidOperationException("Your balance has changed. Wait for the table to update.");
        }
        storage.Prepare(journal);
        Volatile.Write(ref pending, journal);
        await FinishLocked(journal);
    }
    private TableSnapshot View(string playerId, TableAccount account, bool replayed = false)
    {
        var open = state.Tables.Where(table => table.Phase != "closed").ToArray();
        var own = open.SingleOrDefault(table => table.Seats.Any(seat => seat.PlayerId == playerId));
        TableView? view = own == null ? null : new(own.Id, own.Game, own.Phase, own.Version, own.BuyIn, own.Capacity,
            own.Seats.OrderBy(seat => seat.Position).Select(seat => new TableSeatView(seat.Position, seat.Name, seat.Stack, seat.PlayerId == playerId)).ToArray(),
                own.Blackjack == null ? null : Games.Blackjack.View(own.Blackjack), own.Poker == null ? null : Games.Poker.View(own.Poker, own.Seats.Single(seat => seat.PlayerId == playerId).Position), own.Roulette == null ? null : Games.Roulette.View(own.Roulette), own.Elimination == null ? null : Games.RussianRoulette.View(own.Elimination));
        return new(options.Enabled, state.Revision, clock(), 1500, account.Chips, account.Sequence, view, replayed, options.BlackjackDecks, options.TurnSeconds, options.MaxBuyIn, options.MaxWager);
    }
    private static TableDatabase Clone(TableDatabase value) => JsonSerializer.Deserialize<TableDatabase>(JsonSerializer.Serialize(value))!;
    private static int Capacity(string game) => game switch { "blackjack" => Games.Blackjack.MaxPlayers, "poker" => 5, "russian-roulette" => 2, "roulette" => 1, _ => 0 };
    private static void Validate(TableDatabase value)
    {
        if (value.Schema != 2 || value.Revision < 0 || value.Tables == null || value.Actions == null || value.Tables.Select(table => table.Id).Distinct().Count() != value.Tables.Count)
            throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
        if (value.Actions.Any(action => action == null || string.IsNullOrWhiteSpace(action.PlayerId) ||
            !Guid.TryParseExact(action.RequestId, "N", out _) || !Guid.TryParseExact(action.TableId, "N", out _) || string.IsNullOrWhiteSpace(action.Fingerprint)) ||
            value.Actions.Select(action => (action.PlayerId, action.RequestId)).Distinct().Count() != value.Actions.Count)
            throw new InvalidDataException("Invalid or duplicate replay receipt; records preserved.");
        var seated = new HashSet<string>();
        foreach (var table in value.Tables)
        {
            if (!table.Solo || table.Seats?.Count > 1 || table.Capacity != Capacity(table.Game))
                throw new InvalidDataException("Invalid saved table configuration; records preserved.");
            if (!Guid.TryParseExact(table.Id, "N", out _) || !SupportedGames.Contains(table.Game) || table.Version < 1 || table.Capacity is < 1 or > 6 || table.BuyIn < 1 || table.Seats == null || table.Seats.Count > table.Capacity || table.Phase is not ("waiting" or "playing" or "closed"))
                throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
            if (table.Blackjack != null)
            {
                Games.Blackjack.Validate(table.Blackjack);
                if (table.Blackjack.Hands.Count(hand => !hand.Npc) != table.Seats.Count || table.Game != "blackjack" || (table.Phase == "playing") != (table.Blackjack.Phase == "playing") ||
                    table.Phase == "playing" && table.Seats.Any(seat => !table.Blackjack.Hands.Any(hand => !hand.Npc && hand.Position == seat.Position && hand.Bankroll == seat.Stack)))
                    throw new InvalidDataException("Saved hand does not match table phase or bankroll; records preserved.");
            }
            else if (table.Phase == "playing" && table.Poker == null && table.Roulette == null && table.Elimination == null)
                throw new InvalidDataException("Playing table is missing its round; records preserved.");
            if (table.Poker != null)
            {
                Games.Poker.Validate(table.Poker);
                if (table.Poker.Players.Count(player => !player.Npc) != table.Seats.Count || table.Game != "poker" || (table.Phase == "playing") != (table.Poker.Phase == "playing") ||
                    table.Phase == "playing" && table.Seats.Any(seat => !table.Poker.Players.Any(p => !p.Npc && p.Position == seat.Position && p.Bankroll == seat.Stack)))
                    throw new InvalidDataException("Saved hand does not match table phase or bankroll; records preserved.");
            }
            if (table.Roulette != null)
            {
                Games.Roulette.Validate(table.Roulette);
                if (table.Game != "roulette" || (table.Phase == "playing") != (table.Roulette.Phase == "betting") ||
                    table.Phase == "playing" && (table.Roulette.Bets.Any(b => table.Seats.All(s => s.Position != b.Position)) ||
                    table.Seats.Any(s => table.Roulette.Bets.Where(b => b.Position == s.Position).Sum(b => b.Stake) > s.Stack || Games.Roulette.Liability(table.Roulette, s.Position) != s.Liability)))
                    throw new InvalidDataException("Saved roulette phase, stake or liability mismatch; records preserved.");
            }
            if (table.Elimination != null)
            {
                Games.RussianRoulette.Validate(table.Elimination);
                if (table.Elimination.Players.Count(player => !player.Npc) != table.Seats.Count || table.Game != "russian-roulette" || (table.Phase == "playing") != (table.Elimination.Phase == "playing") ||
                    table.Phase == "playing" && table.Seats.Any(s => !table.Elimination.Players.Any(p => !p.Npc && p.Position == s.Position && p.Bankroll == s.Stack)))
                    throw new InvalidDataException("Saved confrontation phase or bankroll mismatch; records preserved.");
            }
            if ((table.Phase == "closed") != (table.Seats.Count == 0))
                throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
            if (table.Seats.Count > 0 && !table.Seats.Any(seat => seat.PlayerId == table.OwnerId))
                throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
            if (table.Seats.Select(seat => seat.Position).Distinct().Count() != table.Seats.Count)
                throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
            foreach (var seat in table.Seats)
                if (string.IsNullOrWhiteSpace(seat.PlayerId) || !seated.Add(seat.PlayerId) || seat.Position < 0 || seat.Position >= table.Capacity || seat.Stack < 0 || seat.Liability < 0 || table.Phase != "playing" && seat.Liability != 0)
                    throw new InvalidDataException("Invalid saved table schema, membership or seat bounds; records preserved.");
        }
    }
    private void ValidateRecovery(TableJournal journal)
    {
        if (journal.State.Revision == state.Revision && JsonSerializer.Serialize(journal.State) == JsonSerializer.Serialize(state))
            return;
        if (journal.State.Revision < 1 || journal.State.Revision - 1 != state.Revision)
            throw new InvalidDataException("Invalid table recovery revision, wallet delta or journal; records preserved.");
        var before = state.Tables.SelectMany(table => table.Seats).ToDictionary(seat => seat.PlayerId, seat => seat.Stack);
        var after = journal.State.Tables.SelectMany(table => table.Seats).ToDictionary(seat => seat.PlayerId, seat => seat.Stack);
        var changes = journal.Wallets.ToDictionary(change => change.PlayerId, change => change.Held);
        foreach (var playerId in before.Keys.Concat(after.Keys).Concat(changes.Keys).Distinct())
            if (after.GetValueOrDefault(playerId) - before.GetValueOrDefault(playerId) != changes.GetValueOrDefault(playerId))
                throw new InvalidDataException("Invalid table recovery revision, wallet delta or journal; records preserved.");
        var beforeLiability = state.Tables.SelectMany(table => table.Seats).ToDictionary(seat => seat.PlayerId, seat => seat.Liability);
        var afterLiability = journal.State.Tables.SelectMany(table => table.Seats).ToDictionary(seat => seat.PlayerId, seat => seat.Liability);
        var liabilities = journal.Wallets.ToDictionary(change => change.PlayerId, change => change.Liability);
        foreach (var playerId in beforeLiability.Keys.Concat(afterLiability.Keys).Concat(liabilities.Keys).Distinct())
            if (afterLiability.GetValueOrDefault(playerId) - beforeLiability.GetValueOrDefault(playerId) != liabilities.GetValueOrDefault(playerId))
                throw new InvalidDataException("Invalid table recovery revision, wallet delta or journal; records preserved.");
    }
    private static void ValidateJournal(TableJournal journal)
    {
        Validate(journal.State);
        if (!Guid.TryParseExact(journal.CommitId, "N", out _) || journal.Wallets == null || journal.Wallets.Select(change => change.PlayerId).Distinct().Count() != journal.Wallets.Length ||
            journal.Wallets.Any(change => string.IsNullOrWhiteSpace(change.PlayerId) || change.Held == long.MinValue || change.Liability == long.MinValue))
            throw new InvalidDataException("Invalid table recovery revision, wallet delta or journal; records preserved.");
    }
}
