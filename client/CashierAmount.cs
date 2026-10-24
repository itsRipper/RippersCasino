using System;
using System.Globalization;

namespace RippersCasino.Client
{
    internal readonly struct CashierAmount
    {
        internal bool Valid { get; }
        internal long Amount { get; }
        internal long CurrencyUnits { get; }
        internal long Chips { get; }
        internal int RemainderRub { get; }
        private CashierAmount(long amount, long currencyUnits, long chips, int remainder) { Valid = true; Amount = amount; CurrencyUnits = currencyUnits; Chips = chips; RemainderRub = remainder; }
        internal static CashierAmount Deposit(string text, int rate, long limit, int reserve)
        {
            if (rate < 1 || reserve < 0 || reserve >= 1000 || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var units) || units < 1 || units > limit) return default;
            try { var value = checked(units * rate + reserve); return new CashierAmount(units, units, value / 1000, (int)(value % 1000)); } catch (OverflowException) { return default; }
        }
        internal static CashierAmount Withdrawal(string text, int rate, long balance, long limit, int reserve)
        {
            if (rate < 1 || reserve < 0 || reserve >= 1000 || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var chips) || chips < 0 || chips > balance) return default;
            try { var value = checked(chips * 1000 + reserve); var units = value / rate; return units < 1 || units > limit ? default : new CashierAmount(chips, units, chips, (int)(value % rate)); } catch (OverflowException) { return default; }
        }
    }
}
