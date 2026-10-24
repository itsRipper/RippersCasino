using System.Text.Json;

namespace RippersCasino.Core.Tables;

public sealed class TableStorage
{
    private readonly string directory;
    public TableStorage(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }
    private string StatePath => Path.Combine(directory, "tables.json");
    private string JournalPath => Path.Combine(directory, "pending.json");
    public TableDatabase Load() => File.Exists(StatePath) ? Read<TableDatabase>(StatePath) : new();
    public TableJournal? Pending() => File.Exists(JournalPath) ? Read<TableJournal>(JournalPath) : null;
    public void Prepare(TableJournal journal) => Write(JournalPath, journal);
    public void Commit(TableDatabase state) => Write(StatePath, state);
    public void Complete() => File.Delete(JournalPath);
    public TableActionReceipt? Receipt(string playerId, string requestId)
    {
        var path = ReceiptPath(playerId, requestId);
        if (!File.Exists(path)) return null;
        var receipt = Read<TableActionReceipt>(path);
        if (receipt.PlayerId != playerId || receipt.RequestId != requestId)
            throw new InvalidDataException("Replay receipt identity mismatch; recovery records were preserved.");
        return receipt;
    }
    public void Archive(TableActionReceipt receipt)
    {
        var previous = Receipt(receipt.PlayerId, receipt.RequestId);
        if (previous != null)
        {
            if (previous != receipt) throw new InvalidDataException("Conflicting replay receipt; recovery records were preserved.");
            return;
        }
        var path = ReceiptPath(receipt.PlayerId, receipt.RequestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Write(path, receipt);
    }
    private string ReceiptPath(string playerId, string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out _)) throw new InvalidDataException("Invalid replay receipt ID.");
        var owner = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(playerId)));
        return Path.Combine(directory, "receipts", owner, requestId + ".json");
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path))
        ?? throw new InvalidDataException("Your saved table could not be opened. Close the casino and check the log.");
    private static void Write<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            file.Write(bytes);
            file.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }
}
