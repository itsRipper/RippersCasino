using RippersCasino.Core.Games;

namespace RippersCasino.Core.Tables;

public sealed class TableOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxBuyIn { get; set; } = 5000;
    public int MaxWager { get; set; } = 1000;
    public int BlackjackDecks { get; set; } = 6;
    public int RouletteBetSeconds { get; set; } = 20;
    public int TurnSeconds { get; set; } = 45;
    public void Validate()
    {
        if (MaxBuyIn is < 1 or > 100000 || MaxWager is < 2 or > 100000 || BlackjackDecks is < 1 or > 8 || TurnSeconds is < 15 or > 300 || RouletteBetSeconds is < 10 or > 120)
            throw new InvalidOperationException("Invalid casino table limits.");
    }
}

public sealed record TableIntent(string RequestId, string Operation, string TableId = "", long Version = 0,
    string Game = "", int Amount = 0, string Selection = "");

public sealed record TableAccount(string Name, long Chips, long Held, bool Available, long Limit = long.MaxValue, long Sequence = 1, long Liability = 0);
public sealed record TableWalletChange(string PlayerId, long Chips, long Held, long Liability = 0);

public interface ITableWallets
{
    Task<IAsyncDisposable> LockAsync(IEnumerable<string> playerIds);
    TableAccount Read(string playerId);
    Task ApplyAsync(TableWalletChange change, string commitId);
}

public sealed class TableSeat
{
    public long Liability { get; set; }
    public bool LeaveAfterRound { get; set; }
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public long Stack { get; set; }
}

public sealed class CasinoTable
{
    public bool Solo { get; set; }
    public EliminationRound? Elimination { get; set; }
    public RouletteRound? Roulette { get; set; }
    public PokerRound? Poker { get; set; }
    public BlackjackRound? Blackjack { get; set; }
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Game { get; set; } = "";
    public string Phase { get; set; } = "waiting";
    public long Version { get; set; } = 1;
    public int BuyIn { get; set; }
    public int Capacity { get; set; }
    public List<TableSeat> Seats { get; set; } = [];
}

public sealed record TableActionReceipt(string PlayerId, string RequestId, string Fingerprint, string TableId);
public sealed class TableDatabase
{
    [System.Text.Json.Serialization.JsonRequired]
    public int Schema { get; set; } = 2;
    public long Revision { get; set; }
    public List<CasinoTable> Tables { get; set; } = [];
    public List<TableActionReceipt> Actions { get; set; } = [];
}
public sealed record TableJournal(string CommitId, TableDatabase State, TableWalletChange[] Wallets);
public sealed record TableSeatView(int Position, string Name, long Stack, bool Mine);
public sealed record TableView(string Id, string Game, string Phase, long Version, int BuyIn, int Capacity, TableSeatView[] Seats, BlackjackView? Blackjack = null, PokerView? Poker = null, RouletteView? Roulette = null, EliminationView? Elimination = null);
public sealed record TableSnapshot(bool Enabled, long Revision, long ServerTime, int PollAfterMs,
    long Balance, long Sequence, TableView? Table, bool Replayed = false, int BlackjackDecks = 6, int TurnSeconds = 45, int MaxBuyIn = 5000, int MaxWager = 1000, int EconomyVersion = 4);
