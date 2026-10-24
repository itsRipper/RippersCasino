using System.Reflection;
using System.Security.Cryptography;
using SPTarkov.Common.Models.Logging;
using System.Text.Json;
using RippersCasino.Core.Tables;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace RippersCasino.Server.Tables;

[Injectable(InjectionType.Singleton)]
public sealed class CasinoTableService
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly TableCoordinator coordinator;
    private readonly ISptLogger<CasinoTableService> logger;
    public static CasinoTableService? Current { get; private set; }
    public CasinoTableService(NativeTableWallets wallets, CasinoSettings settings, ISptLogger<CasinoTableService> logger)
    {
        this.logger = logger;
        coordinator = new(wallets, new TableStorage(Path.Combine("user", "mods", "RippersCasino", "table-data")), settings.Config.Tables);
    }
    public async Task InitializeAsync()
    {
        await coordinator.RecoverAsync();
        Current = this;
    }
    public Task RecoverAsync() => coordinator.RecoverAsync();
    public Task RecoverProfileAsync(MongoId playerId) => coordinator.Blocks(playerId) ? coordinator.RecoverAsync() : Task.CompletedTask;
    public void RequireDurable(MongoId playerId)
    {
        if (coordinator.Blocks(playerId))
            throw new IOException("Your last game is still being saved. Close and reopen the casino to retry.");
    }
    public async ValueTask<string> Poll(MongoId playerId, string since = "")
    {
        try
        {
            return Encode(await coordinator.PollAsync(playerId), since);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            return JsonSerializer.Serialize(new
            {
                error = PlayerError(error),
                retryable = coordinator.RecoveryPending || error is IOException or JsonException
            }, Wire);
        }
    }
    public async ValueTask<string> Act(MongoId playerId, TableIntent request, int economyVersion = 0)
    {
        try
        {
            if (economyVersion != 4) throw new InvalidOperationException("Casino versions do not match. Update the casino installation.");
            return Encode(await coordinator.ActAsync(playerId, request));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException or OverflowException)
        {
            return JsonSerializer.Serialize(new
            {
                error = PlayerError(error),
                retryable = coordinator.RecoveryPending || error is IOException or JsonException
            }, Wire);
        }
    }
    private string PlayerError(Exception error)
    {
        if (error is IOException or JsonException or OverflowException)
            logger?.Error("RippersCasino table: " + error);
        return error is IOException or JsonException || error.Message.EndsWith("records preserved.", StringComparison.Ordinal) ? "Game recovery is required. Your records are saved; check the server log."
            : error is OverflowException ? "That amount is too large. Choose a smaller wager."
            : error.Message;
    }
    private static string Encode(TableSnapshot snapshot, string since = "")
    {
        var cursor = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot with
        {
            ServerTime = 0,
            Replayed = false
        }, Wire)));
        return JsonSerializer.Serialize(new
        {
            state = cursor == since ? null : snapshot,
            cursor,
            unchanged = cursor == since,
            serverTime = snapshot.ServerTime,
            pollAfterMs = snapshot.PollAfterMs
        }, Wire);
    }
}

[Injectable(TypePriority = SPTarkov.Server.Core.DI.OnLoadOrder.SaveCallbacks + 1)]
public sealed class TableRecoveryOnLoad(CasinoTableService tables, ISptLogger<TableRecoveryOnLoad> logger) : SPTarkov.Server.Core.DI.IOnLoad
{
    private int announced;
    public async Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        await tables.InitializeAsync();
        if (Interlocked.Exchange(ref announced, 1) == 0)
        {
            var version = typeof(TableRecoveryOnLoad).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            logger.Info($"[RippersCasino] v{version} loaded successfully.");
        }
    }
}
