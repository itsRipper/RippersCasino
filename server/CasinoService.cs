using RippersCasino.Server.Tables;
using System.Collections.Concurrent;
using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Server.Transactions;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace RippersCasino.Server;

[Injectable(InjectionType.Singleton)]
public sealed class CasinoService(SaveServer saves, JsonUtil json, InventoryHelper inventory, ItemHelper items, CasinoSettings settings, ISptLogger<CasinoService> logger)
{
    private const string WalletKey = "CasinoWallet";
    private readonly ConcurrentDictionary<MongoId, string> protocolErrors = new();
    private readonly ConcurrentDictionary<MongoId, bool> resyncRequired = new();

    public static Wallet ReadWallet(PmcData pmc)
    {
        if (pmc.ExtensionData?.TryGetValue(WalletKey, out var value) != true)
            return new Wallet();
        var saved = JsonSerializer.SerializeToElement(value);
        if (saved.ValueKind != JsonValueKind.Object || !saved.TryGetProperty("Chips", out _) || !saved.TryGetProperty("RemainderRub", out _) || !saved.TryGetProperty("NextSequence", out _) || !saved.TryGetProperty("LastRequestId", out _) || !saved.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var schema) || schema != 4)
            throw new InvalidOperationException("Incompatible casino wallet; saved balance and recovery records were preserved. Use the matching mod version.");
        var wallet = JsonSerializer.Deserialize<Wallet>(saved)
            ?? throw new InvalidOperationException("Your saved balance could not be opened. Close the casino and check the log.");
        wallet.Validate();
        return wallet;
    }

    private PmcData Clone(PmcData pmc) => json.Deserialize<PmcData>(json.Serialize(pmc)!)
        ?? throw new InvalidOperationException("Could not stage RippersCasino transaction.");
    private NativeCurrencyInventory Inventory(PmcData pmc, MongoId id, ItemEventRouterResponse changes) => new(pmc, id, inventory, changes, items, json);
    public async ValueTask<ItemEventRouterResponse> Execute(MongoId id, CasinoRequest request, ItemEventRouterResponse output)
    {
        if (request.EconomyVersion != 4) throw new InvalidOperationException("Casino versions do not match. Update the casino installation.");
        CasinoTableService.Current?.RequireDurable(id);
        // ProfileGate owns the lock for the whole native item-event callback.
        var persistence = CasinoPersistence.Current ?? throw new InvalidOperationException("The casino is not ready. Close and try again.");
        await persistence.EnsureDurableAsync(id);
        if (!saves.ProfileExists(id) || saves.IsProfileInvalidOrUnloadable(id))
            throw new InvalidOperationException("Log in with a PMC to use the casino.");
        var profile = saves.GetProfile(id);
        var pmc = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("Log in with a PMC to use the casino.");
        var command = new CasinoCommand(request.TransactionId, request.Sequence, request.Operation, request.Currency, request.Amount, request.Machine, request.Selection ?? "", request.ExchangeVersion, request.EconomyVersion);
        var wallet = ReadWallet(pmc);
        try
        {
            if (wallet.IsReplay(command))
            {
                // Never replay cashier deltas into an uncertain native client inventory.
                if (wallet.Last?.Error == null && command.Operation is "buy" or "redeem")
                    resyncRequired[id] = true;
                protocolErrors.TryRemove(id, out _);
                return output;
            }
        }
        catch (ArgumentException e) { protocolErrors[id] = e.Message; return output; }
        resyncRequired.TryRemove(id, out _);

        var staged = Clone(pmc);
        var changes = EmptyChanges(id);
        try
        {
            var native = Inventory(staged, id, changes);
            if (!settings.Config.Enabled)
                throw new InvalidOperationException("The casino is closed.");
            if (command.Operation is not ("buy" or "redeem" or "spin" or "race"))
                throw new ArgumentException("Unknown RippersCasino operation.");
            if (!LaptopAvailable(staged))
                throw new InvalidOperationException("Requires Generator level 1 and Intelligence Center level 1.");
            new CasinoEngine(settings.Config, new SecureRandom()).Apply(wallet, command, new DigitalChipInventory(wallet, native, settings.Config.MaxBalance));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
        {
            // Failed native placement/debits are discarded in their entirety.
            staged = Clone(pmc);
            wallet = ReadWallet(pmc);
            changes = EmptyChanges(id);
            CasinoEngine.Complete(wallet, command, new Receipt
            {
                Id = command.TransactionId,
                Fingerprint = command.Fingerprint,
                Operation = command.Operation,
                Currency = command.Currency,
                Amount = command.Amount,
                Balance = wallet.Chips,
                Error = e.Message
            });
        }
        staged.ExtensionData ??= new Dictionary<string, object>();
        staged.ExtensionData[WalletKey] = JsonSerializer.SerializeToElement(wallet);
        NativeCurrencyInventory.Coalesce(changes.ProfileChanges[id].Items!, staged);
        var expectedAggregate = CasinoPersistence.AggregateHash(json.Serialize(staged)!);
        profile.CharacterData!.PmcData = staged;
        persistence.MarkDirty(id, wallet.NextSequence, wallet.LastRequestId, expectedAggregate);
        await persistence.EnsureDurableAsync(id);
        protocolErrors.TryRemove(id, out _);
        var incoming = changes.ProfileChanges[id].Items!;
        var destination = output.ProfileChanges[id].Items!;
        destination.NewItems!.AddRange(incoming.NewItems!);
        destination.ChangedItems!.AddRange(incoming.ChangedItems!);
        destination.DeletedItems!.AddRange(incoming.DeletedItems!);
        return output;
    }

    public async ValueTask<string> State(MongoId id)
    {
        if (CasinoTableService.Current != null)
            await CasinoTableService.Current.RecoverProfileAsync(id);
        var gate = ProfileGate.For(id);
        await gate.WaitAsync();
        try
        {
            CasinoTableService.Current?.RequireDurable(id);
            if (!saves.ProfileExists(id) || saves.IsProfileInvalidOrUnloadable(id))
                return JsonSerializer.Serialize(new
                {
                    error = "Log in with a PMC to use the casino."
                });
            if (CasinoPersistence.Current != null)
                await CasinoPersistence.Current.EnsureDurableAsync(id);
            var pmc = saves.GetProfile(id).CharacterData?.PmcData ?? throw new InvalidOperationException("Log in with a PMC to use the casino.");
            var wallet = ReadWallet(pmc);
            var config = settings.Config;
            var stats = settings.SlotStatistics;
            var last = wallet.Last;
            var native = Inventory(pmc, id, EmptyChanges(id));
            var racingWeight = config.Racing.Racers.Sum(r => r.Weight);
            var balances = config.Cashier.RubPerCurrencyUnit.Keys.ToDictionary(currency => currency, native.Balance);
            return JsonSerializer.Serialize(new
            {
                tableHeld = wallet.TableEscrow,
                enabled = config.Enabled,
                balance = wallet.Chips,
                sequence = wallet.NextSequence,
                available = LaptopAvailable(pmc),
                remainderRub = wallet.RemainderRub,
                chipRubles = CashierTerms.ChipRubles,
                economyVersion = 4,
                cashierVersion = 3,
                currencyBalances = balances,
                machine = config.Machine.Id,
                bets = config.Machine.Bets,
                exchange = config.Cashier.RubPerCurrencyUnit.ToDictionary(p => p.Key, p => new
                {
                    rubPerUnit = p.Value,
                    maxDepositUnits = config.Cashier.MaxDeposit(p.Key, balances[p.Key], CashierTerms.RemainingCapacity(config.MaxBalance, wallet.Chips, wallet.TableEscrow, wallet.TableLiability), wallet.RemainderRub),
                    maxWithdrawalUnits = config.Cashier.MaxWithdrawalUnits(p.Key, wallet.Chips, wallet.RemainderRub)
                }),
                symbols = config.Machine.Symbols,
                weights = config.Machine.Weights,
                triples = config.Machine.Triples,
                twoCherries = config.Machine.TwoCherries,
                rtp = stats.Rtp,
                hitFrequency = stats.HitFrequency,
                redemptionEnabled = config.RedemptionEnabled,
                racing = new
                {
                    enabled = config.Racing.Enabled,
                    machine = RaceConfig.MachineId,
                    racers = config.Racing.Racers.Select(r => new
                    {
                        id = r.Id,
                        name = r.Name,
                        returnMultiplier = r.ReturnMultiplier,
                        winChance = (double)r.Weight / racingWeight,
                        theoreticalReturn = (double)r.Weight * r.ReturnMultiplier / racingWeight
                    })
                },
                last = ReceiptView(last, resyncRequired.GetValueOrDefault(id)),
                lastSlot = ReceiptView(wallet.LastSlot),
                lastRace = ReceiptView(wallet.LastRace),
                error = protocolErrors.GetValueOrDefault(id)
            });
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or JsonException)
        {
            logger.Error("RippersCasino saved state: " + e);
            return JsonSerializer.Serialize(new { error = "Your saved casino data could not be opened. Check the server log." });
        }
        finally { gate.Release(); }
    }

    private static object? ReceiptView(Receipt? r, bool sync = false) => r == null ? null : new
    {
        id = r.Id,
        operation = r.Operation,
        amount = r.Amount,
        currencyUnits = r.CurrencyUnits,
        remainderRub = r.RemainderRub,
        chipDelta = r.ChipDelta,
        currency = r.Currency,
        reels = r.Reels,
        payout = r.Payout,
        balance = r.Balance,
        error = r.Error,
        syncRequired = sync,
        selection = r.Selection,
        winner = r.Winner,
        finishOrder = r.FinishOrder
    };

    public static bool LaptopAvailable(PmcData pmc) => pmc.Hideout?.Areas?.Any(area => (int)area.Type == 4 && area.Level >= 1) == true
        && pmc.Hideout.Areas.Any(area => (int)area.Type == 11 && area.Level >= 1);

    public static ItemEventRouterResponse EmptyChanges(MongoId id) => new()
    {
        Warnings = [],
        ProfileChanges = new Dictionary<MongoId, ProfileChange> { [id] = new() { Items = new ItemChanges { NewItems = [], ChangedItems = [], DeletedItems = [] } } }
    };
}
