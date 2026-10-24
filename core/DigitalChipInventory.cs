namespace RippersCasino.Core;

public sealed class DigitalChipInventory(Wallet wallet, ICurrencyInventory currencies, long limit) : ICurrencyInventory
{
    private long Capacity => checked(limit - wallet.TableEscrow - wallet.TableLiability);
    public long Balance(string currency) => currency == "CHIP" ? wallet.Chips : currencies.Balance(currency);
    public void Debit(string currency, long units)
    {
        if (currency != "CHIP")
        {
            currencies.Debit(currency, units);
            return;
        }
        if (units < 0 || units > wallet.Chips)
            throw new InvalidOperationException("Not enough available chips.");
        wallet.Chips -= units;
    }
    public void Credit(string currency, long units)
    {
        if (currency != "CHIP")
        {
            currencies.Credit(currency, units);
            return;
        }
        if (units < 0 || wallet.Chips > Capacity - units)
            throw new InvalidOperationException("Wallet limit exceeded.");
        wallet.Chips = checked(wallet.Chips + units);
    }
    public void ReservePayout(string currency, long debit, long maxCredit)
    {
        if (currency != "CHIP")
        {
            currencies.ReservePayout(currency, debit, maxCredit);
            return;
        }
        if (debit < 0 || debit > wallet.Chips || maxCredit < 0 || wallet.Chips - debit > Capacity - maxCredit)
            throw new InvalidOperationException("Withdraw some chips or choose a smaller wager.");
    }
}
