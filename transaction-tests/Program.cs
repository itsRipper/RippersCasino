using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using RippersCasino.Server.Transactions;
using HarmonyLib;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;
using ProfileInfo = SPTarkov.Server.Core.Models.Eft.Profile.Info;

internal static partial class Program
{
    private static int assertions;
    private static TaskCompletionSource<string>? heldOperation;
    private static int calls;
    private static string mode = "held";

    public static async Task Main(string[] args)
    {
        var runtime = args.FirstOrDefault() ?? throw new ArgumentException("Pass the compatible SPT_Runtime directory as the first argument.");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(runtime, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        if (args.Contains("--cashier-withdrawal-only")) await WithdrawalTests();
        else if (args.Contains("--table-economy-only")) await TableWalletTests();
        else await Run();
        Console.WriteLine($"PASS: {assertions} transaction integration assertions using installed SPT 4.1.6 binaries.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Run()
    {
        using var fixture = new Fixture();
        await fixture.Saves.SaveProfileAsync(fixture.Id);
        Check(fixture.ReadMoney() == 1000, "baseline actual SaveServer/FileUtil write");
        fixture.Commit(2, "transaction-a", 900);
        fixture.Persistence.MarkDirty(fixture.Id, 2, "transaction-a");
        await fixture.Persistence.EnsureDurableAsync(fixture.Id);
        Check(!fixture.Persistence.IsDirty(fixture.Id), "successful flush clears dirty marker");
        Check(fixture.ReadMoney() == 900, "inventory committed in same profile as wallet");
        fixture.AssertReceipt(2, "transaction-a");

        // Lock the real replacement target: FileUtil can create and flush its
        // temporary file, but the final replacement must fail on Windows.
        fixture.Commit(3, "transaction-b", 800);
        fixture.Persistence.MarkDirty(fixture.Id, 3, "transaction-b");
        using (var blocker = new FileStream(fixture.ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ThrowsAsync<IOException>(() => fixture.Persistence.EnsureDurableAsync(fixture.Id), "failure at final profile replacement");
            Check(fixture.Persistence.IsDirty(fixture.Id), "failed replacement retains pending receipt");
            Check(fixture.Hashes.ContainsKey(fixture.Id), "actual SPT cached hash before failed write");
            Check(fixture.ReadMoney() == 900, "failed replacement preserves complete old profile");
            Check(!File.Exists(fixture.ProfilePath + ".bak"), "actual FileUtil cleans failed temporary file");

            // Demonstrate the installed bug rather than assuming it: a direct
            // unchanged retry reports success while keeping the old disk state.
            await fixture.Saves.SaveProfileAsync(fixture.Id);
            Check(fixture.ReadMoney() == 900, "unmodified SPT retry skips unchanged failed snapshot");
        }
        await fixture.Persistence.EnsureDurableAsync(fixture.Id);
        fixture.AssertReceipt(3, "transaction-b");
        Check(fixture.ReadMoney() == 800, "adapter invalidates hash and persists identical committed transaction");

        // Disk verification must reject even a successfully saved profile whose
        // transaction receipt disagrees with the expected committed receipt.
        fixture.Persistence.MarkDirty(fixture.Id, 4, "wrong-receipt");
        await ThrowsAsync<IOException>(() => fixture.Persistence.EnsureDurableAsync(fixture.Id), "readback receipt mismatch");
        Check(fixture.Persistence.IsDirty(fixture.Id), "mismatch stays blocked");
        fixture.Commit(4, "wrong-receipt", 700);
        await fixture.Persistence.EnsureDurableAsync(fixture.Id, new CancellationToken(true));
        Check(!fixture.Persistence.IsDirty(fixture.Id), "committed save completes despite aborted HTTP cancellation");

        // Parse saved bytes through SPT's own serializer to simulate a restart;
        // this deliberately does not touch game profiles.
        var restored = fixture.Json.Deserialize<SptProfile>(File.ReadAllText(fixture.ProfilePath))!;
        Check(restored.CharacterData!.PmcData!.ExtensionData!["CasinoWallet"] is JsonElement, "profile reload preserves wallet extension data");
        var reloadedSaves = fixture.NewSaveServer();
        reloadedSaves.AddProfile(restored);
        var restarted = new CasinoPersistence(reloadedSaves, fixture.ProfilesPath);
        Check(!restarted.IsDirty(fixture.Id), "fresh process starts with durable aggregate receipt");
        fixture.AssertReceipt(4, "wrong-receipt");

        await GateTests(fixture);
        await SettingsTests();
        await AdapterTests();
        await TableWalletTests();
        await WithdrawalTests();
    }

    private static async Task GateTests(Fixture fixture)
    {
        CasinoPersistence.Current = fixture.Persistence;
        var gatePatch = new Harmony("casino.tests.actual-gate");
        var substitute = new Harmony("casino.tests.callback-fixture");
        var method = typeof(ItemEventCallbacks).GetMethod(nameof(ItemEventCallbacks.HandleEvents))!;
        try
        {
            ProfileGate.Patch(gatePatch);
            substitute.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(CallbackFixture)) { priority = Priority.Last });
            var callback = (ItemEventCallbacks)RuntimeHelpers.GetUninitializedObject(typeof(ItemEventCallbacks));
            heldOperation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            mode = "held";
            calls = 0;
            var first = callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default).AsTask();
            Check(calls == 1 && !first.IsCompleted, "actual patched native entry starts held asynchronous operation");
            var second = Task.Run(async () => await callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default));
            await Task.Delay(100);
            Check(calls == 1 && !second.IsCompleted, "second native entry cannot mutate during first await");
            heldOperation.SetResult("done");
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Check(calls == 2 && ProfileGate.For(fixture.Id).CurrentCount == 1, "async completion releases gate for next native entry");

            mode = "async-error";
            heldOperation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var faulted = callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default).AsTask();
            Check(!faulted.IsCompleted && ProfileGate.For(fixture.Id).CurrentCount == 0, "gate remains held across an incomplete operation that will fault");
            heldOperation.SetException(new IOException("Injected asynchronous callback error."));
            await ThrowsAsync<IOException>(() => faulted, "async callback fault propagates");
            Check(ProfileGate.For(fixture.Id).CurrentCount == 1, "async fault releases gate");
            mode = "sync-error";
            await ThrowsAsync<IOException>(async () => await callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default), "synchronous callback fault propagates");
            Check(ProfileGate.For(fixture.Id).CurrentCount == 1, "synchronous fault releases gate exactly once");

            // A real failed profile save must prevent even the synchronous part
            // of a subsequent native item event from executing.
            mode = "complete";
            var before = calls;
            fixture.Commit(5, "transaction-c", 600);
            fixture.Persistence.MarkDirty(fixture.Id, 5, "transaction-c");
            using (var blocker = new FileStream(fixture.ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ThrowsAsync<IOException>(async () => await callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default), "dirty prefix fails closed before native mutation");
                Check(calls == before, "native mutation did not execute after failed save");
                Check(ProfileGate.For(fixture.Id).CurrentCount == 1, "failed durability prefix releases gate");
            }
            await callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default);
            Check(calls == before + 1, "same dirty snapshot is saved before next native mutation");
            fixture.AssertReceipt(5, "transaction-c");
        }
        finally
        {
            substitute.UnpatchSelf();
            gatePatch.UnpatchSelf();
            CasinoPersistence.Current = null;
        }
    }

    private static bool CallbackFixture(ref ValueTask<string> __result)
    {
        Interlocked.Increment(ref calls);
        if (mode == "sync-error")
            throw new IOException("Injected synchronous callback error.");
        __result = mode switch
        {
            "held" => new ValueTask<string>(heldOperation!.Task),
            "async-error" => new ValueTask<string>(heldOperation!.Task),
            _ => ValueTask.FromResult("done")
        };
        return false;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new Exception("FAILED: " + description);
        assertions++;
        Console.WriteLine("PASS " + description);
    }

    private static async Task ThrowsAsync<T>(Func<Task> operation, string description) where T : Exception
    {
        try
        {
            await operation();
        }
        catch (T) { Check(true, description); return; }
        throw new Exception("FAILED: expected " + typeof(T).Name + ": " + description);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string previousDirectory = Environment.CurrentDirectory;
        private readonly string fixtureParent = Path.Combine(AppContext.BaseDirectory, "fixtures");
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "CasinoTransactionFixture-" + Guid.NewGuid().ToString("N"));
        public MongoId Id { get; } = new();
        public JsonUtil Json { get; } = new([new SptJsonConverterRegistrator()]);
        public SaveServer Saves
        {
            get;
        }
        public CasinoPersistence Persistence
        {
            get;
        }
        public string ProfilesPath => Path.Combine(directory, "user", "profiles");
        public string ProfilePath => Path.Combine(ProfilesPath, $"{Id}.json");
        public ConcurrentDictionary<MongoId, string> Hashes => (ConcurrentDictionary<MongoId, string>)typeof(SaveServer).GetField("saveMd5", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Saves)!;

        public Fixture()
        {
            Directory.CreateDirectory(ProfilesPath);
            Environment.CurrentDirectory = directory;
            Saves = NewSaveServer();
            Saves.AddProfile(new SptProfile { ProfileInfo = new ProfileInfo { ProfileId = Id }, CharacterData = new Characters { PmcData = new PmcData() } });
            Commit(1, "baseline", 1000);
            Persistence = new CasinoPersistence(Saves, ProfilesPath);
        }

        public SaveServer NewSaveServer()
        {
            var config = (CoreConfig)RuntimeHelpers.GetUninitializedObject(typeof(CoreConfig));
            config.Features = (ServerFeatures)RuntimeHelpers.GetUninitializedObject(typeof(ServerFeatures));
            return new(new FileUtil(), [], Json, new HashUtil(null!), null!, null!, null!, config);
        }

        public void Commit(long sequence, string transaction, long money)
        {
            var staged = new PmcData
            {
                Inventory = new BotBaseInventory { Items = [new Item { Id = new MongoId(), Template = "5449016a4bdc2d6f028b456f", Upd = new Upd { StackObjectsCount = money } }] },
                ExtensionData = new Dictionary<string, object> { ["CasinoWallet"] = JsonSerializer.SerializeToElement(new { NextSequence = sequence, LastRequestId = transaction }) }
            };
            Saves.GetProfile(Id).CharacterData!.PmcData = staged;
        }

        public long ReadMoney()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath));
            return document.RootElement.GetProperty("characters").GetProperty("pmc").GetProperty("Inventory").GetProperty("items")[0].GetProperty("upd").GetProperty("StackObjectsCount").GetInt64();
        }

        public void AssertReceipt(long sequence, string transaction)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath));
            var wallet = document.RootElement.GetProperty("characters").GetProperty("pmc").GetProperty("CasinoWallet");
            Check(wallet.GetProperty("NextSequence").GetInt64() == sequence && wallet.GetProperty("LastRequestId").GetString() == transaction, "disk receipt matches " + transaction);
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = previousDirectory;
            // Only this fixture's explicitly created random temporary directory.
            if (Path.GetDirectoryName(directory) == fixtureParent)
                Directory.Delete(directory, true);
        }
    }
}
