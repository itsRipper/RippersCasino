namespace RippersCasino.Core;

public readonly record struct CashierQuote(long CurrencyUnits, long Chips, int RemainderRub);
public sealed class CashierTerms
{
    public const int ChipRubles = 1000;
    public const long MaximumChips = (long.MaxValue - 999) / ChipRubles;
    public Dictionary<string, int> RubPerCurrencyUnit { get; set; } = new() { ["RUB"] = 1, ["USD"] = 136, ["EUR"] = 158 };
    public void Validate()
    {
        if (RubPerCurrencyUnit.Count != 3 || RubPerCurrencyUnit.GetValueOrDefault("RUB") != 1 || RubPerCurrencyUnit.GetValueOrDefault("USD") != 136 || RubPerCurrencyUnit.GetValueOrDefault("EUR") != 158)
            throw new InvalidOperationException("Casino rates must be RUB 1, USD 136 and EUR 158 per currency unit; one chip is 1000 RUB.");
    }
    public CashierQuote Deposit(string currency, long units, int remainder, long capacity)
    {
        ValidateRemainder(remainder);
        if (units < 1 || capacity < 0 || capacity > MaximumChips) throw new ArgumentException("Invalid deposit amount.");
        var rubles = checked(units * Rate(currency) + remainder);
        var chips = rubles / ChipRubles;
        if (chips > capacity) throw new InvalidOperationException("Wallet capacity exceeded.");
        return new(units, chips, (int)(rubles % ChipRubles));
    }
    public CashierQuote Withdrawal(string currency, long chips, long available, int remainder)
    {
        ValidateRemainder(remainder);
        if (chips < 0 || chips > available || chips > MaximumChips) throw new ArgumentException("Choose available whole chips to withdraw.");
        var rubles = checked(chips * ChipRubles + remainder);
        var rate = Rate(currency);
        var units = rubles / rate;
        if (units == 0) throw new InvalidOperationException("Not enough value for one " + currency + ".");
        return new(units, chips, (int)(rubles % rate));
    }
    public long MaxDeposit(string currency, long funds, long capacity, int remainder)
    {
        ValidateRemainder(remainder);
        if (capacity < 0 || capacity > MaximumChips) throw new ArgumentException("Invalid wallet capacity.");
        var headroom = checked(capacity * ChipRubles + (ChipRubles - 1 - remainder));
        return Math.Min(Math.Max(0, funds), headroom / Rate(currency));
    }
    public long MaxWithdrawalUnits(string currency, long chips, int remainder)
    {
        ValidateRemainder(remainder);
        if (chips < 0 || chips > MaximumChips) throw new ArgumentException("Invalid wallet value.");
        return checked(chips * ChipRubles + remainder) / Rate(currency);
    }
    private int Rate(string currency) => RubPerCurrencyUnit.TryGetValue(currency, out var rate) && rate is >= 1 and <= ChipRubles ? rate : throw new ArgumentException("Unsupported currency.");
    private static void ValidateRemainder(int remainder)
    {
        if (remainder is < 0 or >= ChipRubles) throw new InvalidOperationException("Invalid cashier reserve.");
    }
    public static long RemainingCapacity(long limit, long chips, long escrow, long liability)
    {
        if (limit < 0 || chips < 0 || escrow < 0 || liability < 0) throw new ArgumentException("Invalid wallet capacity values.");
        if (escrow > limit || liability > limit - escrow) return 0;
        var available = limit - escrow - liability;
        return chips >= available ? 0 : available - chips;
    }
}
