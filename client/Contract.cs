using System.Collections.Generic;

namespace RippersCasino.Client
{
    // Lower-case wire names deliberately match the native item-event contract.
    internal sealed class CasinoRequest
    {
        public string Action = "RippersCasino";
        public string transactionId;
        public long sequence;
        public int exchangeVersion, economyVersion;
        public string operation;
        public string currency;
        public long amount;
        public string machine = "rippers-slot";
        public string selection;
    }
    internal sealed class ExchangeRate
    {
        public int rubPerUnit;
        public long maxDepositUnits, maxWithdrawalUnits;
    }
    internal sealed class RacerState
    {
        public string id, name;
        public int returnMultiplier;
    }
    internal sealed class RacingState
    {
        public bool enabled;
        public string machine;
        public RacerState[] racers;
    }
    internal sealed class CasinoState
    {
        public bool enabled;
        public bool available;
        public int economyVersion, chipRubles, remainderRub;
        public long tableHeld;
        public Dictionary<string, long> currencyBalances = new Dictionary<string, long>();
        public long balance;
        public long sequence;
        public int[] bets = { 1, 5, 10, 25, 100 };
        public bool redemptionEnabled = true;
        public int cashierVersion;
        public Dictionary<string, ExchangeRate> exchange = new Dictionary<string, ExchangeRate>();
        public string[] symbols = { "7", "Diamond", "BAR", "Bell", "Cherry", "Lemon" };
        public int[] triples;
        public int twoCherries;
        public CasinoReceipt last, lastSlot, lastRace;
        public RacingState racing;

        public string error;
    }
    internal sealed class CasinoReceipt
    {
        public long currencyUnits, chipDelta;
        public int remainderRub;
        public bool syncRequired;
        public string id;
        public string operation;
        public long amount;
        public string currency;
        public string[] reels;
        public string selection, winner;
        public string[] finishOrder;
        public long payout;
        public long balance;
        public string error;
    }
}

