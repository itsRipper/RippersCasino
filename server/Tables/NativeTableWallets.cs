using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Core.Tables;
using RippersCasino.Server.Transactions;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace RippersCasino.Server.Tables;

[Injectable(InjectionType.Singleton)]
public sealed class NativeTableWallets(SaveServer saves, JsonUtil json, CasinoSettings settings) : ITableWallets
{
    public async Task<IAsyncDisposable> LockAsync(IEnumerable<string> playerIds)
    {
        var acquired = new List<SemaphoreSlim>();
        try
        {
            foreach (var playerId in playerIds.Distinct().OrderBy(id => id, StringComparer.Ordinal))
            {
                var id = Id(playerId);
                var gate = ProfileGate.For(id);
                await gate.WaitAsync();
                acquired.Add(gate);
                await Persistence.EnsureDurableAsync(id);
            }
            return new Lease(acquired);
        }
        catch
        {
            foreach (var gate in acquired.AsEnumerable().Reverse())
                gate.Release();
            throw;
        }
    }
    private static CasinoPersistence Persistence => CasinoPersistence.Current
        ?? throw new InvalidOperationException("Casino persistence is not initialized.");
    private static MongoId Id(string playerId)
    {
        if (playerId.Length != 24 || !playerId.All(Uri.IsHexDigit))
            throw new ArgumentException("Log in with a PMC to use the casino.");
        return new MongoId(playerId);
    }
    private SPTarkov.Server.Core.Models.Eft.Common.PmcData Pmc(string playerId)
    {
        var id = Id(playerId);
        if (!saves.ProfileExists(id) || saves.IsProfileInvalidOrUnloadable(id))
            throw new InvalidOperationException("Log in with a PMC to use the casino.");
        return saves.GetProfile(id).CharacterData?.PmcData ?? throw new InvalidOperationException("Log in with a PMC to use the casino.");
    }
    public TableAccount Read(string playerId)
    {
        var pmc = Pmc(playerId);
        var wallet = CasinoService.ReadWallet(pmc);
        return new(pmc.Info?.Nickname ?? "Casino guest", wallet.Chips, wallet.TableEscrow,
            settings.Config.Enabled && CasinoService.LaptopAvailable(pmc), settings.Config.MaxBalance, wallet.NextSequence, wallet.TableLiability);
    }
    public async Task ApplyAsync(TableWalletChange change, string commitId)
    {
        var id = Id(change.PlayerId);
        var pmc = Pmc(change.PlayerId);
        var wallet = CasinoService.ReadWallet(pmc);
        if (wallet.AdditionalData?.TryGetValue("CasinoTableReceipt", out var marker) == true &&
            marker.TryGetProperty("CommitId", out var previous) && previous.GetString() == commitId)
        {
            await Persistence.EnsureDurableAsync(id);
            return;
        }
        var chips = checked(wallet.Chips + change.Chips);
        var held = checked(wallet.TableEscrow + change.Held);
        var liability = checked(wallet.TableLiability + change.Liability);
        if (chips < 0 || held < 0 || liability < 0 || held > settings.Config.MaxBalance || liability > settings.Config.MaxBalance - held || chips > settings.Config.MaxBalance - held - liability)
            throw new InvalidOperationException("Your bankroll could not be restored. Close the casino and check the log.");
        wallet.Chips = chips;
        wallet.AdditionalData ??= new();
        wallet.AdditionalData["CasinoTableReceipt"] = JsonSerializer.SerializeToElement(new
        {
            CommitId = commitId,
            Escrow = held,
            Liability = liability
        });
        var command = new CasinoCommand(commitId, wallet.NextSequence, "table", "RUB", Math.Abs(change.Chips), "casino-tables");
        CasinoEngine.Complete(wallet, command, new Receipt { Id = commitId, Fingerprint = command.Fingerprint, Operation = "table", Amount = command.Amount, Balance = chips, ChipDelta = change.Chips });
        var staged = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Common.PmcData>(json.Serialize(pmc)!)
            ?? throw new InvalidOperationException("Cannot stage casino table wallet.");
        staged.ExtensionData ??= new();
        staged.ExtensionData["CasinoWallet"] = JsonSerializer.SerializeToElement(wallet);
        var aggregate = CasinoPersistence.AggregateHash(json.Serialize(staged)!);
        saves.GetProfile(id).CharacterData!.PmcData = staged;
        Persistence.MarkDirty(id, wallet.NextSequence, commitId, aggregate);
        await Persistence.EnsureDurableAsync(id);
    }
    private sealed class Lease(List<SemaphoreSlim> gates) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            foreach (var gate in gates.AsEnumerable().Reverse())
                gate.Release();
            gates.Clear();
            return ValueTask.CompletedTask;
        }
    }
}
