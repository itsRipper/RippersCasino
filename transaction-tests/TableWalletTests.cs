using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using RippersCasino.Core;
using RippersCasino.Core.Tables;
using RippersCasino.Server;
using RippersCasino.Server.Tables;
using RippersCasino.Server.Transactions;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using ProfileInfo = SPTarkov.Server.Core.Models.Eft.Profile.Info;

internal static partial class Program
{
    private static async Task TableWalletTests()
    {
        using var fixture = new Fixture();
        var second = new MongoId();
        fixture.Saves.AddProfile(new SptProfile { ProfileInfo = new ProfileInfo { ProfileId = second }, CharacterData = new Characters { PmcData = new PmcData() } });
        foreach (var id in new[] { fixture.Id, second })
        {
            var pmc = fixture.Saves.GetProfile(id).CharacterData!.PmcData!;
            pmc.Hideout = new()
            {
                Areas = [new() { Type = (HideoutAreas)4, Level = 1 }, new() { Type = (HideoutAreas)11, Level = 1 }]
            };
            pmc.ExtensionData = new()
            {
                ["CasinoWallet"] = JsonSerializer.SerializeToElement(new Wallet { Chips = 100, RemainderRub = 496 })
            };
            await fixture.Saves.SaveProfileAsync(id);
        }
        var settings = new CasinoSettings();
        var wallets = new NativeTableWallets(fixture.Saves, fixture.Json, settings);
        CasinoPersistence.Current = fixture.Persistence;
        long now = 1000;
        var storagePath = Path.Combine(fixture.ProfilesPath, "table-tests");
        var coordinator = new TableCoordinator(wallets, new(storagePath), new(), () => now);
        var service = (CasinoTableService)RuntimeHelpers.GetUninitializedObject(typeof(CasinoTableService));
        typeof(CasinoTableService).GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, coordinator);
        var patch = new Harmony("casino.tests.table-durability-gate");
        try
        {
            await service.InitializeAsync();
            ProfileGate.Patch(patch);
            var beforeInventory = fixture.Json.Serialize(fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!.Inventory);
            TableIntent Intent(string operation, TableView? table = null) => new(Guid.NewGuid().ToString("N"), operation, table?.Id ?? "", table?.Version ?? 0, operation == "solo" ? "poker" : "", operation == "solo" ? 20 : 0);
            var create = Intent("solo");
            var a = await coordinator.ActAsync(fixture.Id, create);
            var b = await coordinator.ActAsync(second, Intent("solo"));
            Check(a.Balance == 80 && b.Balance == 80 && b.Table!.Seats.Length == 1, "native profiles reserve independent solo bankrolls");
            Check(CasinoService.ReadWallet(fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!).RemainderRub == 496, "native table reservation preserves the independent RUB reserve");
            Check(wallets.Read(fixture.Id).Held == 20 && wallets.Read(second).Held == 20, "native wallet persists each independent reservation");
            var firstPath = Path.Combine(fixture.ProfilesPath, fixture.Id + ".json");
            var secondPath = Path.Combine(fixture.ProfilesPath, second + ".json");
            var firstBytes = File.ReadAllBytes(firstPath);
            var replay = await coordinator.ActAsync(fixture.Id, create);
            Check(replay.Replayed && File.ReadAllBytes(firstPath).SequenceEqual(firstBytes), "native table action replay does not write or debit the wallet again");
            Check(fixture.Json.Serialize(fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!.Inventory) == beforeInventory, "table reservations never mutate native inventory objects");
            var reloaded = fixture.Json.Deserialize<SptProfile>(File.ReadAllText(firstPath))!.CharacterData!.PmcData!;
            Check(CasinoService.ReadWallet(reloaded).TableEscrow == 20 && CasinoService.ReadWallet(reloaded).Chips == 80, "SPT serializer reload retains liquid chips and escrow together");
            Check(!JsonSerializer.Serialize(await coordinator.PollAsync(fixture.Id)).Contains(second.ToString()), "native table projection does not expose another backend profile ID");
            var pmc = fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!;
            var wallet = CasinoService.ReadWallet(pmc);
            var liquid = new DigitalChipInventory(wallet, new RejectNativeInventory(), 110);
            await ThrowsAsync<InvalidOperationException>(() => Task.Run(() => liquid.Credit("CHIP", 11)), "table escrow protects later refund capacity from other games and deposits");
            await coordinator.ActAsync(fixture.Id, Intent("leave", a.Table));
            await coordinator.ActAsync(second, Intent("leave", b.Table));
            Check(wallets.Read(fixture.Id).Chips == 100 && wallets.Read(second).Chips == 100 && wallets.Read(fixture.Id).Held == 0 && wallets.Read(second).Held == 0, "independent solo cashouts release each original reservation exactly once");
            firstBytes = File.ReadAllBytes(firstPath);
            var secondBytes = File.ReadAllBytes(secondPath);
            var restarted = new TableCoordinator(wallets, new(storagePath), new(), () => now);
            await restarted.RecoverAsync();
            Check(File.ReadAllBytes(firstPath).SequenceEqual(firstBytes) && File.ReadAllBytes(secondPath).SequenceEqual(secondBytes), "completed solo cashouts are unchanged by restart");
            var blackjack = new TableCoordinator(wallets, new(storagePath), new(), () => now, new BlackjackTestShuffle());
            typeof(CasinoTableService).GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, blackjack);
            await service.InitializeAsync();
            a = await blackjack.ActAsync(fixture.Id, new(Guid.NewGuid().ToString("N"), "solo", Game: "blackjack", Amount: 20));
            a = await blackjack.ActAsync(fixture.Id, new(Guid.NewGuid().ToString("N"), "start", a.Table!.Id, a.Table.Version, Amount: 2));
            reloaded = fixture.Json.Deserialize<SptProfile>(File.ReadAllText(firstPath))!.CharacterData!.PmcData!;
            Check(CasinoService.ReadWallet(reloaded).TableLiability == 4, "native solo Blackjack persists maximum payout headroom");
            var settle = Intent("stand", a.Table);
            using (var blocker = new FileStream(firstPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ThrowsAsync<IOException>(() => blackjack.ActAsync(fixture.Id, settle), "native solo settlement handles an actual profile replacement failure");
                Check(blackjack.Blocks(fixture.Id), "unflushed solo settlement blocks its profile");
                var callback = (ItemEventCallbacks)RuntimeHelpers.GetUninitializedObject(typeof(ItemEventCallbacks));
                await ThrowsAsync<IOException>(async () => await callback.HandleEvents("fixture", new ItemEventRouterRequest(), fixture.Id, default), "native item events recover pending solo settlement before acquiring their gate");
                Check(ProfileGate.For(fixture.Id).CurrentCount == 1, "failed solo recovery releases the native profile gate");
            }
            await blackjack.RecoverAsync();
            a = await blackjack.ActAsync(fixture.Id, settle);
            var profit = a.Table!.Blackjack!.Hands.Single(hand => !hand.Npc).Profit;
            Check(a.Replayed && wallets.Read(fixture.Id).Held == 20 + profit, "solo settlement replays without duplicate winnings");
            Check(wallets.Read(fixture.Id).Liability == 0, "solo settlement releases all payout capacity holds");
            await blackjack.ActAsync(fixture.Id, Intent("leave", a.Table));
            Check(wallets.Read(fixture.Id).Chips == 100 + profit && wallets.Read(fixture.Id).Held == 0, "solo cashout publishes earned chips to the original native wallet");
            Check(fixture.Json.Serialize(fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!.Inventory) == beforeInventory, "blackjack never manufactures native inventory deltas");
        }
        finally
        {
            patch.UnpatchSelf();
            typeof(CasinoTableService).GetProperty(nameof(CasinoTableService.Current))!.SetValue(null, null);
            CasinoPersistence.Current = null;
        }
    }
    private sealed class BlackjackTestShuffle : IRandomSource
    {
        public int Next(int exclusiveMax) => exclusiveMax - 1;
    }
    private sealed class RejectNativeInventory : ICurrencyInventory
    {
        public long Balance(string currency) => throw new Exception("Native inventory unexpectedly accessed");
        public void Debit(string currency, long units) => throw new Exception("Native inventory unexpectedly accessed");
        public void Credit(string currency, long units) => throw new Exception("Native inventory unexpectedly accessed");
        public void ReservePayout(string currency, long debit, long maximum) => throw new Exception("Native inventory unexpectedly accessed");
    }
}
