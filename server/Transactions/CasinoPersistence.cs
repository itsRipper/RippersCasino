using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace RippersCasino.Server.Transactions;

/// <summary>
/// Keeps committed in-memory transactions blocked until their aggregate profile
/// snapshot is saved. A failed flush never causes the transaction to run again.
/// </summary>
public sealed class CasinoPersistence
{
    private readonly SaveServer saveServer;
    private readonly ConcurrentDictionary<MongoId, string> saveHashes;
    private readonly ConcurrentDictionary<MongoId, PendingReceipt> dirty = new();
    private readonly string profilesDirectory;

    public static CasinoPersistence? Current { get; set; }

    public CasinoPersistence(SaveServer saveServer, string? profilesDirectory = null)
    {
        this.saveServer = saveServer;
        this.profilesDirectory = Path.GetFullPath(profilesDirectory ?? Path.Combine("user", "profiles"));

        // SPT 4.1.6 caches the hash before awaiting its file write. Without
        // invalidation an unchanged retry after an I/O failure can skip saving.
        // Fail startup if this version-specific compatibility point changes.
        var hashesField = typeof(SaveServer).GetField("saveMd5", BindingFlags.Instance | BindingFlags.NonPublic);
        saveHashes = hashesField?.GetValue(saveServer) as ConcurrentDictionary<MongoId, string>
            ?? throw new NotSupportedException("Casino requires SPT 4.1.6 SaveServer.saveMd5 compatibility.");
    }

    /// <remarks>Call while holding ProfileGate, immediately after the PMC pointer swap.</remarks>
    public void MarkDirty(MongoId sessionId, long expectedSequence, string expectedTxn, string? expectedAggregate = null)
    {
        if (sessionId.IsEmpty || expectedSequence < 1 || string.IsNullOrWhiteSpace(expectedTxn))
            throw new ArgumentException("Invalid casino persistence receipt.");

        var receipt = new PendingReceipt(expectedSequence, expectedTxn, expectedAggregate);
        if (!dirty.TryAdd(sessionId, receipt) && dirty[sessionId] != receipt)
            throw new InvalidOperationException("Your last payment is still being saved. Close and reopen the casino to retry.");
    }

    public bool IsDirty(MongoId sessionId) => dirty.ContainsKey(sessionId);

    /// <summary>Caller must hold the profile gate. A failed save leaves the receipt dirty.</summary>
    public async Task EnsureDurableAsync(MongoId sessionId, CancellationToken cancellationToken = default)
    {
        if (!dirty.TryGetValue(sessionId, out var expected))
            return;

        // Once committed in memory, finishing the save must outlive an aborted
        // HTTP request. The caller remains behind the gate until this completes.
        _ = cancellationToken;
        try
        {
            saveHashes.TryRemove(sessionId, out _);
            await saveServer.SaveProfileAsync(sessionId, CancellationToken.None).ConfigureAwait(false);

            var profilePath = Path.Combine(profilesDirectory, $"{sessionId}.json");
            await using var stream = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (!TryGetReceipt(document.RootElement, out var saved) || saved.Sequence != expected.Sequence || saved.Transaction != expected.Transaction
                || (expected.Aggregate != null && AggregateHash(document.RootElement.GetProperty("characters").GetProperty("pmc").GetRawText()) != expected.Aggregate))
                throw new IOException("Your payment could not be saved. Choose Resume before playing again.");
        }
        catch (UnauthorizedAccessException error)
        {
            throw new IOException("Your character is being saved. Choose Resume to try again.", error);
        }
        catch (JsonException error)
        {
            throw new IOException("Your payment could not be saved. Choose Resume before playing again.", error);
        }

        // The gate prevents replacement while saving. Compare anyway so this
        // adapter cannot accidentally clear a later transaction's dirty marker.
        ((ICollection<KeyValuePair<MongoId, PendingReceipt>>)dirty).Remove(new(sessionId, expected));
    }

    private static bool TryGetReceipt(JsonElement root, out PendingReceipt receipt)
    {
        receipt = default;
        if (!root.TryGetProperty("characters", out var characters)
            || !characters.TryGetProperty("pmc", out var pmc)
            || !pmc.TryGetProperty("CasinoWallet", out var wallet)
            || !wallet.TryGetProperty("NextSequence", out var sequence)
            || !sequence.TryGetInt64(out var sequenceValue)
            || !wallet.TryGetProperty("LastRequestId", out var transaction)
            || transaction.ValueKind != JsonValueKind.String)
            return false;

        receipt = new PendingReceipt(sequenceValue, transaction.GetString()!, null);
        return true;
    }

    // Read-back must bind the currency inventory, wallet receipt and hideout state.
    public static string AggregateHash(string pmcJson)
    {
        using var document = JsonDocument.Parse(pmcJson);
        var pmc = document.RootElement;
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            foreach (var name in new[] { "inventory", "CasinoWallet", "hideout" })
            {
                var property = pmc.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                writer.WritePropertyName(name);
                Canonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    private static void Canonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Canonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray())
                    Canonical(writer, value);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
    private readonly record struct PendingReceipt(long Sequence, string Transaction, string? Aggregate);
}
