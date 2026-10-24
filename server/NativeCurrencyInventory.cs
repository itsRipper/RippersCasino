using RippersCasino.Core;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils;

namespace RippersCasino.Server;

// Native inventory mutations stay on a staged PMC clone until the operation succeeds.
public sealed class NativeCurrencyInventory(PmcData staged, MongoId session, InventoryHelper helper, ItemEventRouterResponse changes, ItemHelper items, JsonUtil json) : ICurrencyInventory
{
    public static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>
    {
        ["RUB"] = "5449016a4bdc2d6f028b456f",
        ["USD"] = "5696686a4bdc2da3298b456a",
        ["EUR"] = "569668774bdc2da2298b4568"
    };

    public long Balance(string currency) => checked(StashMoney(currency).Sum(StackCount));

    public void ReservePayout(string currency, long debit, long maxCredit)
    {
        // The worst outcome must fit before any random ticket is drawn. Testing
        // placement against an independent clone avoids turning a jackpot into
        // a rejected/losing spin when the stash is full.
        var probe = json.Deserialize<PmcData>(json.Serialize(staged)!) ?? throw new InvalidOperationException("Cannot reserve payout space.");
        var reserve = new NativeCurrencyInventory(probe, session, helper, CasinoService.EmptyChanges(session), items, json);
        reserve.Debit(currency, debit);
        reserve.Credit(currency, maxCredit);
    }

    public void Debit(string currency, long units)
    {
        if (units < 0)
            throw new ArgumentException("Invalid inventory debit.");
        if (units == 0)
            return;
        var stacks = StashMoney(currency).ToList();
        if (checked(stacks.Sum(StackCount)) < units)
            throw new InvalidOperationException("Insufficient " + currency + " in your stash.");
        foreach (var stack in stacks)
        {
            var take = (int)Math.Min(units, StackCount(stack));
            helper.RemoveItemByCount(staged, stack.Id, take, session, changes);
            CheckWarnings();
            units -= take;
            if (units == 0)
                return;
        }
        throw new InvalidOperationException("Inventory debit did not complete.");
    }

    public void Credit(string currency, long units)
    {
        if (units < 0)
            throw new ArgumentException("Invalid inventory credit.");
        if (units == 0)
            return;
        var template = items.GetItem(Templates[currency]);
        var stackLimit = checked((long)(template.Value?.Properties?.StackMaxSize ?? throw new InvalidOperationException("This currency is unavailable.")));
        if (!template.Key || stackLimit < 1 || stackLimit > int.MaxValue)
            throw new InvalidOperationException("This currency is unavailable.");
        foreach (var stack in StashMoney(currency))
        {
            var add = Math.Min(units, stackLimit - StackCount(stack));
            if (add <= 0)
                continue;
            stack.Upd ??= new Upd();
            stack.Upd.StackObjectsCount = StackCount(stack) + add;
            changes.ProfileChanges[session].Items!.ChangedItems!.Add(stack);
            units -= add;
            if (units == 0)
                return;
        }
        while (units > 0)
        {
            var count = Math.Min(units, stackLimit);
            var item = new Item { Id = new MongoId(), Template = Templates[currency], Upd = new Upd { StackObjectsCount = count } };
            helper.AddItemToStash(session, new AddItemDirectRequest { ItemWithModsToAdd = [item], FoundInRaid = false, UseSortingTable = false }, staged, changes);
            CheckWarnings();
            if (!staged.Inventory!.Items!.Any(i => i.Id == item.Id))
                throw new InvalidOperationException("Not enough stash space for the complete payout. Free stash space and try again.");
            units -= count;
        }
    }

    public IEnumerable<Item> StashMoney(string currency)
    {
        var template = Templates.GetValueOrDefault(currency) ?? throw new ArgumentException("Unsupported currency.");
        var inventory = staged.Inventory ?? throw new InvalidOperationException("Your stash could not be opened. Close and reopen the casino.");
        var entries = inventory.Items ?? throw new InvalidOperationException("Your stash could not be opened. Close and reopen the casino.");
        var byId = entries.ToDictionary(i => i.Id.ToString());
        var occupied = entries.Where(i => i.ParentId != null).Select(i => i.ParentId!).ToHashSet();
        bool InStash(Item item)
        {
            var parent = item.ParentId;
            var seen = new HashSet<string>();
            while (parent != null && seen.Add(parent))
            {
                if (parent == inventory.Stash?.ToString())
                    return true;
                if (!byId.TryGetValue(parent, out var ancestor))
                    return false;
                parent = ancestor.ParentId;
            }
            return false;
        }
        return entries.Where(i => i.Template.ToString() == template && InStash(i) && !occupied.Contains(i.Id.ToString())).OrderBy(StackCount).ThenBy(i => i.Id.ToString()).ToList();
    }

    // Native helpers may emit several changes to one stack during stake+payout.
    // Emit each final object once and never emit change/new for a deleted ID.
    public static void Coalesce(ItemChanges changes, PmcData final)
    {
        var byId = final.Inventory!.Items!.ToDictionary(i => i.Id);
        var deleted = changes.DeletedItems!.Select(i => i.Id).Distinct().ToHashSet();
        var created = changes.NewItems!.Select(i => i.Id).Where(byId.ContainsKey).ToHashSet();
        changes.DeletedItems = deleted.Where(i => !byId.ContainsKey(i)).Select(i => new DeletedItem { Id = i }).ToList();
        changes.NewItems = created.Select(i => byId[i]).ToList();
        changes.ChangedItems = changes.ChangedItems!.Select(i => i.Id).Distinct().Where(i => !created.Contains(i) && byId.ContainsKey(i)).Select(i => byId[i]).ToList();
    }

    public static long StackCount(Item item)
    {
        var count = item.Upd?.StackObjectsCount ?? 1;
        if (count < 1 || count > int.MaxValue || count != Math.Truncate(count))
            throw new InvalidOperationException("Invalid inventory stack.");
        return checked((long)count);
    }
    private void CheckWarnings()
    {
        if (changes.Warnings?.Count > 0)
            throw new InvalidOperationException(changes.Warnings[0].ErrorMessage ?? "Your stash could not be updated. Close and reopen the casino.");
    }
}
