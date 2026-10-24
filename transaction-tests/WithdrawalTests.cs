using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Server;
using RippersCasino.Server.Transactions;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Utils.Cloners;

internal static partial class Program
{
    private static async Task WithdrawalTests()
    {
        using var fixture = new Fixture();
        var table = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        var stashTemplate = new MongoId();
        var moneyParent = new MongoId();
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(table, new Dictionary<MongoId, TemplateItem>
        {
            [stashTemplate] = new() { Id = stashTemplate, Properties = new() { Grids = [new() { Properties = new() { CellsH = 2, CellsV = 2 } }] } },
            [moneyParent] = new() { Id = moneyParent, Parent = default }
        });
        foreach (var currency in new[] { "RUB", "USD", "EUR" })
        {
            var tpl = NativeCurrencyInventory.Templates[currency];
            table.Items[tpl] = new() { Id = tpl, Type = "Item", Parent = moneyParent, Properties = new() { StackMaxSize = 1000, Width = 1, Height = 1 } };
        }
        var bases = new ItemBaseClassService(DispatchProxy.Create<ISptLogger<ItemBaseClassService>, QuietLog>(), table, null!);
        var itemHelper = new ItemHelper(null!, table, null!, null!, bases, null!, null!, null!, null!);
        var logger = DispatchProxy.Create<ISptLogger<InventoryHelper>, QuietLog>();
        var locale = (ServerLocalisationService)RuntimeHelpers.GetUninitializedObject(typeof(ServerLocalisationService));
        typeof(ServerLocalisationService).GetProperty("ServerLocale")!.SetValue(locale, "en");
        typeof(ServerLocalisationService).GetProperty("LocalesHydrated")!.SetValue(locale, true);
        typeof(ServerLocalisationService).GetProperty("LoadedLocales", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(locale, new Dictionary<string, LazyLoad<Dictionary<string, string>>>());
        var helper = new InventoryHelper(logger, new HttpResponseUtil(fixture.Json, locale), null!, null!, itemHelper, locale, null!, new FastCloner());
        var stash = new MongoId();
        var pmc = new PmcData
        {
            Inventory = new() { Stash = stash, SortingTable = new MongoId(), Items = [new() { Id = stash, Template = stashTemplate }] },
            Bonuses = [], InsuredItems = [],
            Hideout = new() { Areas = [new() { Type = (SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas)4, Level = 1 }, new() { Type = (SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas)11, Level = 1 }] },
            ExtensionData = new() { ["CasinoWallet"] = JsonSerializer.SerializeToElement(new Wallet { Chips = 100, RemainderRub = 997 }) }
        };
        fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData = pmc;
        await fixture.Saves.SaveProfileAsync(fixture.Id);
        CasinoPersistence.Current = fixture.Persistence;
        var service = new CasinoService(fixture.Saves, fixture.Json, helper, itemHelper, new CasinoSettings(), DispatchProxy.Create<SPTarkov.Common.Models.Logging.ISptLogger<CasinoService>, QuietLog>());
        PmcData Current() => fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!;
        var terms = new CashierTerms();
        foreach (var currency in new[] { "RUB", "USD", "EUR" })
        {
            var before = CasinoService.ReadWallet(Current()).Chips;
            var adapter = new NativeCurrencyInventory(Current(), fixture.Id, helper, CasinoService.EmptyChanges(fixture.Id), itemHelper, fixture.Json);
            Check(adapter.Balance(currency) == 0, currency + " reproduction starts with no destination stack");
            using var state = JsonDocument.Parse(await service.State(fixture.Id));
            var exchange = state.RootElement.GetProperty("exchange").GetProperty(currency);
            Check(exchange.GetProperty("maxDepositUnits").GetInt64() == 0 && exchange.GetProperty("maxWithdrawalUnits").GetInt64() > 0, currency + " withdrawal availability is independent of deposit funds");
            var reserveBefore = CasinoService.ReadWallet(Current()).RemainderRub;
            var budget = currency == "RUB" ? 0 : 1;
            var quote = terms.Withdrawal(currency, budget, before, reserveBefore);
            var request = new CasinoRequest { TransactionId = Guid.NewGuid().ToString("N"), Sequence = CasinoService.ReadWallet(Current()).NextSequence, Operation = "redeem", Currency = currency, Amount = budget, Machine = "rippers-slot", ExchangeVersion = 3, EconomyVersion = 4 };
            var result = await service.Execute(fixture.Id, request, CasinoService.EmptyChanges(fixture.Id));
            var wallet = CasinoService.ReadWallet(Current());
            Check(wallet.Last!.Error == null, currency + " new-stack withdrawal succeeds: " + wallet.Last.Error);
            Check(wallet.Chips == before - budget && wallet.RemainderRub == quote.RemainderRub && wallet.Last.CurrencyUnits == quote.CurrencyUnits, currency + " exact chips debited, remainder retained");
            var created = result.ProfileChanges[fixture.Id].Items!.NewItems!.Single();
            Check(created.Template.ToString() == NativeCurrencyInventory.Templates[currency] && created.ParentId == stash.ToString() && created.Location != null && created.Upd!.StackObjectsCount == quote.CurrencyUnits, currency + " native stash delta contains complete positioned new currency stack");
        }
        CasinoRequest Withdraw(string currency, long chips) => new() { TransactionId = Guid.NewGuid().ToString("N"), Sequence = CasinoService.ReadWallet(Current()).NextSequence, Operation = "redeem", Currency = currency, Amount = chips, Machine = "rippers-slot", ExchangeVersion = 3, EconomyVersion = 4 };
        var inventoryBefore = fixture.Json.Serialize(Current().Inventory);
        var chipsBefore = CasinoService.ReadWallet(Current()).Chips;
        var rejected = Withdraw("RUB", 2);
        var failed = await service.Execute(fixture.Id, rejected, CasinoService.EmptyChanges(fixture.Id));
        Check(CasinoService.ReadWallet(Current()).Last!.Error != null, "insufficient space rejects payout after partial merge and new-stack placement");
        Check(fixture.Json.Serialize(Current().Inventory) == inventoryBefore && CasinoService.ReadWallet(Current()).Chips == chipsBefore, "failed placement rolls back all native stacks and chip debit");
        Check(failed.ProfileChanges[fixture.Id].Items!.NewItems!.Count == 0 && failed.ProfileChanges[fixture.Id].Items!.ChangedItems!.Count == 0, "failed payout publishes no partial inventory deltas");
        var saved = File.ReadAllBytes(fixture.ProfilePath);
        await service.Execute(fixture.Id, rejected, CasinoService.EmptyChanges(fixture.Id));
        Check(File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(saved), "rejected withdrawal replay is idempotent");
        await service.Execute(fixture.Id, Withdraw("RUB", 0), CasinoService.EmptyChanges(fixture.Id));
        Check(CasinoService.ReadWallet(Current()).Last!.Error == null && CasinoService.ReadWallet(Current()).Chips == chipsBefore && CasinoService.ReadWallet(Current()).RemainderRub == 0, "smaller payout succeeds within remaining native stash capacity");
        var usdStack = Current().Inventory!.Items!.Single(i => i.Template.ToString() == NativeCurrencyInventory.Templates["USD"]);
        usdStack.Template = NativeCurrencyInventory.Templates["RUB"];
        usdStack.Upd!.StackObjectsCount = 1000;
        inventoryBefore = fixture.Json.Serialize(Current().Inventory);
        chipsBefore = CasinoService.ReadWallet(Current()).Chips;
        await service.Execute(fixture.Id, Withdraw("USD", 1), CasinoService.EmptyChanges(fixture.Id));
        Check(CasinoService.ReadWallet(Current()).Last!.Error != null && CasinoService.ReadWallet(Current()).Chips == chipsBefore && fixture.Json.Serialize(Current().Inventory) == inventoryBefore, "zero destination currency plus full stash rejects cleanly without losing chips");
        var funding = Current().Inventory!.Items!.First(i => i.Template.ToString() == NativeCurrencyInventory.Templates["RUB"]);
        funding.Template = NativeCurrencyInventory.Templates["USD"];
        funding.Upd!.StackObjectsCount = 136;
        var deposit = new CasinoRequest { TransactionId = Guid.NewGuid().ToString("N"), Sequence = CasinoService.ReadWallet(Current()).NextSequence, Operation = "buy", Currency = "USD", Amount = 136, Machine = "rippers-slot", ExchangeVersion = 3, EconomyVersion = 4 };
        chipsBefore = CasinoService.ReadWallet(Current()).Chips;
        await service.Execute(fixture.Id, deposit, CasinoService.EmptyChanges(fixture.Id));
        var deposited = CasinoService.ReadWallet(Current());
        Check(deposited.Last!.Error == null && deposited.Chips == chipsBefore + 18 && deposited.RemainderRub == 496, "native deposit debits 136 USD and persists exactly 18 chips plus 496 RUB reserve");
        saved = File.ReadAllBytes(fixture.ProfilePath);
        await service.Execute(fixture.Id, deposit, CasinoService.EmptyChanges(fixture.Id));
        Check(File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(saved), "native deposit replay preserves chips and reserve without duplicate funding");
        deposit.EconomyVersion = 3;
        await ThrowsAsync<InvalidOperationException>(() => service.Execute(fixture.Id, deposit, CasinoService.EmptyChanges(fixture.Id)).AsTask(), "old denomination requests cannot enter the new native economy");
        Check(File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(saved), "rejected old protocol preserves all native profile bytes");
    }
    public class QuietLog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.ReturnType == typeof(bool) ? false : null;
    }
}
