namespace RippersCasino.Core;

public interface ICurrencyInventory
{
    long Balance(string currency);
    void Debit(string currency, long units);
    void Credit(string currency, long units);
    void ReservePayout(string currency, long debit, long maxCredit);
}
public sealed class CasinoEngine(CasinoConfig config, IRandomSource random)
{
    private readonly SlotRules slots = new(config.Machine);
    public Receipt Apply(Wallet wallet, CasinoCommand command, ICurrencyInventory inventory)
    {
        wallet.Validate();
        if (command.EconomyVersion != 4) throw new InvalidOperationException("Casino versions do not match. Update the casino installation.");
        if (wallet.IsReplay(command))
            return wallet.Last!;
        if (command.Operation is "buy" or "redeem" && command.ExchangeVersion != 3)
            throw new InvalidOperationException("The cashier has been updated. Reopen the casino; no money was exchanged.");
        if (command.ExchangeVersion != 0 && (command.ExchangeVersion != 3 || command.Operation is not ("buy" or "redeem")))
            throw new ArgumentException("Casino versions do not match. Update the casino installation.");
        if (!config.Enabled)
            throw new InvalidOperationException("The casino is closed.");
        if (!config.Cashier.RubPerCurrencyUnit.ContainsKey(command.Currency))
            throw new ArgumentException("Unsupported currency.");
        if (command.Machine != (command.Operation == "race" ? RaceConfig.MachineId : config.Machine.Id))
            throw new ArgumentException("Unknown machine.");
        if (command.Operation != "race" && !string.IsNullOrEmpty(command.Selection))
            throw new ArgumentException("Selection is only supported for races.");
        var balance = inventory.Balance("CHIP");
        var result = new Receipt { Id = command.TransactionId, Fingerprint = command.Fingerprint, Operation = command.Operation, Currency = command.Currency, Amount = command.Amount };
        switch (command.Operation)
        {
            case "buy":
                var capacity = CashierTerms.RemainingCapacity(config.MaxBalance, balance, wallet.TableEscrow, wallet.TableLiability);
                var deposit = config.Cashier.Deposit(command.Currency, command.Amount, wallet.RemainderRub, capacity);
                inventory.Debit(command.Currency, deposit.CurrencyUnits);
                inventory.Credit("CHIP", deposit.Chips);
                wallet.RemainderRub = deposit.RemainderRub;
                result.CurrencyUnits = deposit.CurrencyUnits;
                result.ChipDelta = deposit.Chips;
                break;
            case "redeem":
                if (!config.RedemptionEnabled) throw new InvalidOperationException("Withdrawals are unavailable.");
                var withdrawal = config.Cashier.Withdrawal(command.Currency, command.Amount, balance, wallet.RemainderRub);
                inventory.Debit("CHIP", withdrawal.Chips);
                inventory.Credit(command.Currency, withdrawal.CurrencyUnits);
                wallet.RemainderRub = withdrawal.RemainderRub;
                result.CurrencyUnits = withdrawal.CurrencyUnits;
                result.ChipDelta = -withdrawal.Chips;
                break;
            case "race":
                if (!config.Racing.Enabled)
                    throw new InvalidOperationException("Boss Racing is closed.");
                if (!config.Machine.Bets.Any(bet => bet == command.Amount))
                    throw new ArgumentException("Choose one of the available wagers.");
                var selection = config.Racing.Racers.SingleOrDefault(r => r.Id == command.Selection) ?? throw new ArgumentException("Unknown racer.");
                if (balance < command.Amount)
                    throw new InvalidOperationException("Not enough available chips.");
                var liability = checked((long)command.Amount * selection.ReturnMultiplier);
                if (balance - command.Amount > config.MaxBalance - liability)
                    throw new InvalidOperationException("Withdraw some chips or choose a smaller wager.");
                inventory.ReservePayout("CHIP", command.Amount, liability);
                result.Selection = selection.Id;
                result.FinishOrder = config.Racing.Finish(random);
                result.Winner = result.FinishOrder[0];
                result.Payout = result.Winner == selection.Id ? liability : 0;
                inventory.Debit("CHIP", command.Amount);
                if (result.Payout > 0)
                    inventory.Credit("CHIP", result.Payout);
                break;
            case "spin":
                if (!config.Machine.Bets.Any(bet => bet == command.Amount))
                    throw new ArgumentException("Choose one of the available wagers.");
                if (balance < command.Amount)
                    throw new InvalidOperationException("Not enough available chips.");
                var maxMultiplier = Math.Max(config.Machine.Triples.Max(), config.Machine.TwoCherries);
                var maxPayout = checked((long)command.Amount * maxMultiplier);
                if (balance - command.Amount > config.MaxBalance - maxPayout)
                    throw new InvalidOperationException("Withdraw some chips or choose a smaller wager.");
                // Reserve the maximum wallet liability before consuming RNG.
                // A winning result must never fail capacity checks after the roll.
                inventory.ReservePayout("CHIP", command.Amount, maxPayout);
                result.Reels = slots.Spin(random);
                result.Payout = checked((long)command.Amount * slots.Multiplier(result.Reels));
                inventory.Debit("CHIP", command.Amount);
                if (result.Payout > 0)
                    inventory.Credit("CHIP", result.Payout);
                break;
            default:
                throw new ArgumentException("Unknown RippersCasino operation.");
        }
        result.Balance = inventory.Balance("CHIP");
        result.RemainderRub = wallet.RemainderRub;
        result.ChipDelta = checked(result.Balance - balance);
        Complete(wallet, command, result);
        return result;
    }
    public static void Complete(Wallet wallet, CasinoCommand command, Receipt result)
    {
        result.RemainderRub = wallet.RemainderRub;
        wallet.NextSequence = checked(wallet.NextSequence + 1);
        wallet.LastRequestId = command.TransactionId;
        wallet.Last = result;
        if (result.Error == null && command.Operation == "spin")
            wallet.LastSlot = result;
        if (result.Error == null && command.Operation == "race")
            wallet.LastRace = result;
    }
}
