using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Server;
using RippersCasino.Server.Transactions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Path = System.IO.Path;

internal static partial class Program
{
    private static async Task AdapterTests()
    {
        using var fixture = new Fixture();
        foreach (var data in new[] { "{\"Chips\":7654}", "{\"Version\":3,\"Chips\":7654}", "{\"Version\":4,\"RemainderRub\":0,\"NextSequence\":1,\"LastRequestId\":\"\"}" })
        {
            var incompatible = new PmcData { ExtensionData = new() { ["CasinoWallet"] = JsonSerializer.Deserialize<JsonElement>(data) } };
            var beforeWallet = JsonSerializer.Serialize(incompatible);
            await ThrowsAsync<InvalidOperationException>(() => Task.Run(() => CasinoService.ReadWallet(incompatible)), "incomplete or incompatible wallet fails explicitly");
            Check(JsonSerializer.Serialize(incompatible) == beforeWallet, "rejected wallet read preserves original saved state");
        }
        var templateTable = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templateTable, new Dictionary<MongoId, TemplateItem>
        {
            [NativeCurrencyInventory.Templates["RUB"]] = new() { Id = NativeCurrencyInventory.Templates["RUB"], Properties = new() { StackMaxSize = 10000 } }
        });
        var itemHelper = new ItemHelper(null!, templateTable, null!, null!, null!, null!, null!, null!, null!);
        var inventoryHelper = new InventoryHelper(null!, null!, null!, null!, itemHelper, null!, null!, null!);
        var stash = new MongoId();
        var equipped = new MongoId();
        var container = new Item { Id = new MongoId(), Template = new MongoId(), ParentId = stash.ToString() };
        Item Stack(long count, string parent) => new()
        {
            Id = new MongoId(),
            Template = NativeCurrencyInventory.Templates["RUB"],
            ParentId = parent,
            Upd = new()
            {
                StackObjectsCount = count
            }
        };
        var small = Stack(20, stash.ToString());
        var nested = Stack(100, container.Id.ToString());
        var worn = Stack(500, equipped.ToString());
        var cycle1 = Stack(700, "000000000000000000000002");
        cycle1.Id = "000000000000000000000001";
        var cycle2 = new Item { Id = "000000000000000000000002", Template = new MongoId(), ParentId = cycle1.Id.ToString() };
        var pmc = new PmcData { Inventory = new BotBaseInventory { Stash = stash, Equipment = equipped, Items = [container, small, nested, worn, cycle1, cycle2] }, InsuredItems = [] };
        var changes = CasinoService.EmptyChanges(fixture.Id);
        var adapter = new NativeCurrencyInventory(pmc, fixture.Id, inventoryHelper, changes, itemHelper, fixture.Json);
        Check(adapter.Balance("RUB") == 120, "actual adapter counts direct and nested stash stacks, excluding equipped and cyclic ancestry");
        Check(adapter.StashMoney("RUB").Select(i => i.Id).SequenceEqual(new[] { small.Id, nested.Id }), "actual adapter orders partial stacks before larger stacks");
        adapter.Debit("RUB", 25);
        Check(!pmc.Inventory.Items.Contains(small) && nested.Upd!.StackObjectsCount == 95, "actual installed RemoveItemByCount deletes exhausted stack and partially removes next stack");
        Check(changes.ProfileChanges[fixture.Id].Items!.DeletedItems!.Single().Id == small.Id, "native whole-stack removal emits deletion");
        Check(changes.ProfileChanges[fixture.Id].Items!.ChangedItems!.Single().Id == nested.Id, "native partial removal emits changed stack");
        adapter.Credit("RUB", 10);
        adapter.Credit("RUB", 15);
        Check(nested.Upd!.StackObjectsCount == 120 && worn.Upd!.StackObjectsCount == 500, "actual credit merges physical stash stack and leaves equipped stack unchanged");
        var eventItems = changes.ProfileChanges[fixture.Id].Items!;
        NativeCurrencyInventory.Coalesce(eventItems, pmc);
        Check(eventItems.ChangedItems!.Count == 1 && ReferenceEquals(eventItems.ChangedItems[0], nested), "actual coalesce emits the final native stack once across stake and credits");
        var removedNew = Stack(1, stash.ToString());
        eventItems.NewItems!.AddRange([nested, nested, removedNew]);
        eventItems.ChangedItems.AddRange([nested, small, removedNew]);
        eventItems.DeletedItems!.AddRange([new() { Id = small.Id }, new() { Id = nested.Id }, new() { Id = removedNew.Id }]);
        NativeCurrencyInventory.Coalesce(eventItems, pmc);
        Check(eventItems.NewItems.Count == 1 && eventItems.NewItems[0].Id == nested.Id && eventItems.ChangedItems.Count == 0, "coalesce keeps final new object and removes overlapping changed/dead new entries");
        Check(eventItems.DeletedItems.Count == 2 && eventItems.DeletedItems.All(i => i.Id != nested.Id), "coalesce deduplicates deleted IDs and suppresses deletion of final live stack");
        var beforeReject = adapter.Balance("RUB");
        await ThrowsAsync<InvalidOperationException>(() => Task.Run(() => adapter.Debit("RUB", beforeReject + 1)), "insufficient debit rejected before native mutation");
        Check(adapter.Balance("RUB") == beforeReject, "insufficient debit preserves physical stacks");
        await ThrowsAsync<ArgumentException>(() => Task.Run(() => adapter.Credit("RUB", -1)), "negative native credit rejected");
        await AggregateTests(fixture);
        await ReservationValidationTests();
        await DigitalServiceTests(fixture, inventoryHelper, itemHelper, templateTable);
    }

    private static async Task AggregateTests(Fixture fixture)
    {
        fixture.Commit(10, "aggregate-receipt", 600);
        var current = fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!;
        var expected = CasinoPersistence.AggregateHash(fixture.Json.Serialize(current)!);
        current.Inventory!.Items![0].Upd!.StackObjectsCount = 601;
        fixture.Persistence.MarkDirty(fixture.Id, 10, "aggregate-receipt", expected);
        await ThrowsAsync<IOException>(() => fixture.Persistence.EnsureDurableAsync(fixture.Id), "real SaveServer receipt matches but same-ID physical quantity mismatch fails verification");
        Check(fixture.Persistence.IsDirty(fixture.Id) && fixture.ReadMoney() == 601, "aggregate mismatch keeps profile blocked despite matching receipt on disk");
        current.Inventory.Items[0].Upd!.StackObjectsCount = 600;
        await fixture.Persistence.EnsureDurableAsync(fixture.Id);
        Check(!fixture.Persistence.IsDirty(fixture.Id) && fixture.ReadMoney() == 600, "same adapter verifies original full aggregate and clears pending receipt only after correction");
        var originalId = current.Inventory.Items[0].Id;
        current.Inventory.Items[0].Id = new MongoId();
        Check(CasinoPersistence.AggregateHash(fixture.Json.Serialize(current)!) != expected, "aggregate binds physical stack identity as well as quantities");
        current.Inventory.Items[0].Id = originalId;
        var reordered = "{\"CasinoWallet\":{\"LastRequestId\":\"x\",\"NextSequence\":2},\"Inventory\":{\"items\":[]}}";
        var original = "{\"Inventory\":{\"items\":[]},\"CasinoWallet\":{\"NextSequence\":2,\"LastRequestId\":\"x\"}}";
        Check(CasinoPersistence.AggregateHash(reordered) == CasinoPersistence.AggregateHash(original), "aggregate hash canonicalizes property order");
        Check(CasinoPersistence.AggregateHash(original) != CasinoPersistence.AggregateHash(original.Replace("\"NextSequence\":2", "\"NextSequence\":3")), "aggregate binds persisted receipt marker");
    }

    private static async Task ReservationValidationTests()
    {
        var damaged = new Wallet { Chips = 80, AdditionalData = new() { ["CasinoTableReceipt"] = JsonSerializer.SerializeToElement(new { CommitId = "saved", Escrow = 20 }) } };
        var saved = JsonSerializer.Serialize(damaged);
        await ThrowsAsync<InvalidOperationException>(() => Task.Run(damaged.Validate), "incomplete reservation marker is not interpreted as zero liability");
        Check(JsonSerializer.Serialize(damaged) == saved, "incomplete reservation evidence is preserved");
    }
    private static async Task DigitalServiceTests(Fixture fixture, InventoryHelper helper, ItemHelper items, TemplateTable templates)
    {
        string[] currencies = ["RUB", "USD", "EUR"];
        string[] money = ["5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568"];
        foreach (var tpl in money)
            templates.Items[tpl] = new()
            {
                Id = tpl,
                Properties = new()
                {
                    StackMaxSize = 500000,
                    Width = 1,
                    Height = 1
                }
            };
        var stash = new MongoId();
        var pmc = new PmcData { Inventory = new BotBaseInventory { Stash = stash, Items = [] }, InsuredItems = [], Hideout = new Hideout { Areas = [new() { Type = (SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas)4, Level = 1 }, new() { Type = (SPTarkov.Server.Core.Models.Enums.Hideout.HideoutAreas)11, Level = 1 }] }, ExtensionData = new() { ["CasinoWallet"] = JsonSerializer.SerializeToElement(new Wallet()) } };
        foreach (var tpl in money)
            pmc.Inventory.Items.Add(new()
            {
                Id = new MongoId(),
                Template = tpl,
                ParentId = stash.ToString(),
                Upd = new()
                {
                    StackObjectsCount = 100000
                }
            });
        fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData = pmc;
        await fixture.Saves.SaveProfileAsync(fixture.Id);
        CasinoPersistence.Current = fixture.Persistence;
        var service = new CasinoService(fixture.Saves, fixture.Json, helper, items, new CasinoSettings(), DispatchProxy.Create<SPTarkov.Common.Models.Logging.ISptLogger<CasinoService>, QuietLog>());
        PmcData Current() => fixture.Saves.GetProfile(fixture.Id).CharacterData!.PmcData!;
        Wallet CurrentWallet() => CasinoService.ReadWallet(Current());
        long Currency(string tpl) => Current().Inventory!.Items!.Where(i => i.Template.ToString() == tpl).Sum(i => checked((long)(i.Upd?.StackObjectsCount ?? 1)));
        CasinoRequest Command(string op, int amount = 0, string currency = "RUB") => new()
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            Sequence = CurrentWallet().NextSequence,
            Operation = op,
            EconomyVersion = 4,
            ExchangeVersion = op is "buy" or "redeem" ? 3 : 0,
            Amount = amount,
            Currency = currency,
            Machine = "rippers-slot"
        };
        async Task Send(CasinoRequest request)
        {
            var gate = ProfileGate.For(fixture.Id);
            await gate.WaitAsync();
            try
            {
                await service.Execute(fixture.Id, request, CasinoService.EmptyChanges(fixture.Id));
            }
            finally { gate.Release(); }
        }
        try
        {
            var beforeState = fixture.Json.Serialize(Current());
            var beforeStateBytes = File.ReadAllBytes(fixture.ProfilePath);
            using (var state = JsonDocument.Parse(await service.State(fixture.Id)))
            {
                Check(state.RootElement.GetProperty("economyVersion").GetInt32() == 4 && state.RootElement.GetProperty("chipRubles").GetInt32() == 1000, "native state uses the new denomination");
            }
            Check(fixture.Json.Serialize(Current()) == beforeState && File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(beforeStateBytes), "state query preserves the profile aggregate");
            var bytes = File.ReadAllBytes(fixture.ProfilePath);
            var terms = new CashierTerms();
            foreach (var currency in currencies)
            {
                var before = CurrentWallet().Chips;
                var reserve = CurrentWallet().RemainderRub;
                var cash = Currency(money[Array.IndexOf(currencies, currency)]);
                var units = currency == "RUB" ? 10000 : currency == "USD" ? 136 : 158;
                var quote = terms.Deposit(currency, units, reserve, settingsCapacity());
                await Send(Command("buy", units, currency));
                Check(CurrentWallet().Last!.Error == null && CurrentWallet().Chips == before + quote.Chips && CurrentWallet().RemainderRub == quote.RemainderRub && Currency(money[Array.IndexOf(currencies, currency)]) == cash - units, "actual digital deposit preserves native " + currency + " value including the reserve");
            }
            foreach (var currency in currencies)
            {
                var before = CurrentWallet().Chips;
                var cash = Currency(money[Array.IndexOf(currencies, currency)]);
                var quote = terms.Withdrawal(currency, 5, before, CurrentWallet().RemainderRub);
                await Send(Command("redeem", 5, currency));
                Check(CurrentWallet().Last!.Error == null && CurrentWallet().Chips == before - 5 && CurrentWallet().RemainderRub == quote.RemainderRub && Currency(money[Array.IndexOf(currencies, currency)]) == cash + quote.CurrencyUnits, "actual digital withdrawal credits native " + currency + " from the new denomination");
            }
            long settingsCapacity() => CashierTerms.MaximumChips;
            var unchanged = CurrentWallet().Chips;
            var tooMuch = Command("redeem", 10000);
            await Send(tooMuch);
            Check(CurrentWallet().Last!.Error != null && CurrentWallet().Chips == unchanged, "insufficient digital balance records failure without losing chips");
            var a = Command("buy", 2000);
            var b = Command("buy", 2000);
            var cashBefore = Currency(money[0]);
            var chipsBefore = CurrentWallet().Chips;
            await Task.WhenAll(Send(a), Send(b));
            Check(CurrentWallet().Chips == chipsBefore + 2 && Currency(money[0]) == cashBefore - 2000, "concurrent digital requests at one sequence settle once under native gate");
            var winningSpin = Command("spin", 1);
            var initial = CurrentWallet().Chips;
            await Send(winningSpin);
            var settled = CurrentWallet();
            Check(settled.Last!.Reels.Length == 3 && settled.Chips == initial - 1 + settled.Last.Payout, "actual digital spin conserves stake and authoritative payout");
            bytes = File.ReadAllBytes(fixture.ProfilePath);
            await Send(winningSpin);
            Check(File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(bytes) && CurrentWallet().Chips == settled.Chips, "digital spin replay returns durable result without another wager");
            var race = Command("race", 1);
            race.Machine = RaceConfig.MachineId;
            race.Selection = "killa";
            var raceBefore = CurrentWallet().Chips;
            var raceCash = money.Select(Currency).ToArray();
            await Send(race);
            var raceWallet = CurrentWallet();
            Check(raceWallet.Last!.Error == null && raceWallet.Last.FinishOrder.Length == 6 && raceWallet.Last.FinishOrder.Distinct().Count() == 6 && raceWallet.Last.Winner == raceWallet.Last.FinishOrder[0], "actual race records one server winner and a complete unique finish order");
            Check(raceWallet.Chips == raceBefore - 1 + raceWallet.Last!.Payout && raceWallet.Last.Payout == (raceWallet.Last.Winner == "killa" ? 2 : 0) && money.Select(Currency).SequenceEqual(raceCash), "actual race conserves digital stake and payout without native currency changes");
            bytes = File.ReadAllBytes(fixture.ProfilePath);
            await Send(race);
            Check(File.ReadAllBytes(fixture.ProfilePath).SequenceEqual(bytes) && CurrentWallet().Chips == raceWallet.Chips, "actual race replay changes neither profile bytes nor chip balance");
            var raceReload = fixture.Json.Deserialize<SptProfile>(File.ReadAllText(fixture.ProfilePath))!.CharacterData!.PmcData!;
            Check(CasinoService.ReadWallet(raceReload).LastRace!.Id == race.TransactionId && CasinoService.ReadWallet(raceReload).LastSlot!.Id == winningSpin.TransactionId, "native save reload preserves race receipt and independent previous slot result");
            var raceState = await service.State(fixture.Id);
            using (var state = JsonDocument.Parse(raceState))
            {
                var root = state.RootElement;
                var racers = root.GetProperty("racing").GetProperty("racers").EnumerateArray().ToArray();
                var config = new CasinoConfig();
                var total = config.Racing.Racers.Sum(racer => racer.Weight);
                Check(racers.Length == 6 && racers.Select(racer => racer.GetProperty("id").GetString()).SequenceEqual(config.Racing.Racers.Select(racer => racer.Id)),
                    "native state exposes the six authoritative racers in contract order");
                Check(racers.Select((racer, index) =>
                    racer.GetProperty("returnMultiplier").GetInt32() == config.Racing.Racers[index].ReturnMultiplier &&
                    racer.GetProperty("winChance").GetDouble() == (double)config.Racing.Racers[index].Weight / total &&
                    racer.GetProperty("theoreticalReturn").GetDouble() == (double)config.Racing.Racers[index].Weight * config.Racing.Racers[index].ReturnMultiplier / total).All(valid => valid),
                    "native race state retains exact numeric prices and probability quotes");
                Check(root.GetProperty("lastRace").GetProperty("id").GetString() == race.TransactionId &&
                    root.GetProperty("lastSlot").GetProperty("id").GetString() == winningSpin.TransactionId &&
                    root.GetProperty("balance").GetInt64() == raceWallet.Chips,
                    "state reports independent saved receipts and the authoritative chip balance");
            }
            var parallelRace = Command("race", 1);
            parallelRace.Machine = RaceConfig.MachineId;
            parallelRace.Selection = "tagilla";
            var parallelSpin = Command("spin", 1);
            var parallelBefore = CurrentWallet().Chips;
            var parallelSequence = CurrentWallet().NextSequence;
            await Task.WhenAll(Send(parallelRace), Send(parallelSpin));
            Check(CurrentWallet().NextSequence == parallelSequence + 1 && CurrentWallet().Chips == parallelBefore - 1 + CurrentWallet().Last!.Payout, "native gate accepts one wager when slot and racing requests share a sequence");
            Check(CurrentWallet().Last!.Id == parallelRace.TransactionId || CurrentWallet().Last!.Id == parallelSpin.TransactionId, "mixed-game contention retains the original winning request identity");
            var durableRace = Command("race", 1);
            durableRace.Machine = RaceConfig.MachineId;
            durableRace.Selection = "killa";
            var raceFailureBefore = CurrentWallet().Chips;
            using (var blocker = new FileStream(fixture.ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ThrowsAsync<IOException>(() => Send(durableRace), "race catches native profile replacement failure");
                Check(fixture.Persistence.IsDirty(fixture.Id) && CurrentWallet().Last!.Id == durableRace.TransactionId && CurrentWallet().Chips == raceFailureBefore - 1 + CurrentWallet().Last!.Payout, "failed race save retains one exact staged finish order and wallet");
            }
            var stagedRace = JsonSerializer.Serialize(CurrentWallet().Last);
            var stagedRaceBalance = CurrentWallet().Chips;
            var stagedRaceSequence = CurrentWallet().NextSequence;
            await Send(durableRace);
            Check(!fixture.Persistence.IsDirty(fixture.Id) && JsonSerializer.Serialize(CurrentWallet().Last) == stagedRace && CurrentWallet().Chips == stagedRaceBalance && CurrentWallet().NextSequence == stagedRaceSequence, "race durability retry replays the original finish order without another wager");
            var deposit = Command("buy", 363, "USD");
            var beforeFailure = CurrentWallet().Chips;
            var cashBeforeFailure = Currency(money[1]);
            var failureQuote = terms.Deposit("USD", 363, CurrentWallet().RemainderRub, CashierTerms.MaximumChips);
            using (var blocker = new FileStream(fixture.ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ThrowsAsync<IOException>(() => Send(deposit), "digital deposit catches actual native profile replacement failure");
                Check(fixture.Persistence.IsDirty(fixture.Id) && CurrentWallet().Chips == beforeFailure + failureQuote.Chips && Currency(money[1]) == cashBeforeFailure - 363, "failed save retains a single pending currency-wallet aggregate");
            }
            await Send(deposit);
            Check(!fixture.Persistence.IsDirty(fixture.Id) && CurrentWallet().Chips == beforeFailure + failureQuote.Chips && Currency(money[1]) == cashBeforeFailure - 363, "retry finishes same digital deposit without duplicate debit or credit");
            var restored = fixture.Json.Deserialize<SptProfile>(File.ReadAllText(fixture.ProfilePath))!.CharacterData!.PmcData!;
            Check(CasinoService.ReadWallet(restored).Chips == CurrentWallet().Chips, "native profile reload retains final digital wallet");
            Check(Current().Inventory!.Items!.All(item => money.Contains(item.Template.ToString())), "digital play and exchange create only native currencies");
        }
        finally { CasinoPersistence.Current = null; }
    }

}
