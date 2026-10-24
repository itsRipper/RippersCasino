namespace RippersCasino.Client
{
    internal sealed class TableRequest
    {
        public string requestId, operation, tableId = "", game = "", selection = "";
        public long version;
        public int amount, economyVersion;
    }
    internal sealed class TableSeat
    {
        public int position;
        public string name;
        public long stack;
        public bool mine;
    }
    internal sealed class TableView
    {
        public string id, game, phase;
        public long version;
        public TableSeat[] seats;
        public BlackjackView blackjack;
        public PokerView poker;
        public RouletteView roulette;
        public EliminationView elimination;
    }
    internal sealed class BlackjackHand
    {
        public int position, bet, total;
        public string name, status;
        public bool npc;
        public int[] cards;
        public long profit;
    }
    internal sealed class BlackjackView
    {
        public string id, phase;
        public int turn, dealerTotal;
        public long deadline;
        public int[] dealer;
        public BlackjackHand[] hands;
    }
    internal sealed class PokerPlayer
    {
        public int position;
        public string name, action, result;
        public bool npc, folded, allIn;
        public long stack, bet, profit;
        public int[] cards;
    }
    internal sealed class PokerView
    {
        public string id, phase, street;
        public int button, turn, bigBlind;
        public long deadline, pot, call, minRaise, maxRaise;
        public bool canRaise;
        public int[] board;
        public string[] events;
        public PokerPlayer[] players;
    }
    internal sealed class RouletteBet
    {
        public int position;
        public string selection;
        public long stake;
    }
    internal sealed class RouletteView
    {
        public string id, phase;
        public long deadline, closedAt;
        public int number;
        public RouletteBet[] bets;
        public System.Collections.Generic.Dictionary<int, long> profits;
    }
    internal sealed class EliminationPlayer
    {
        public int position;
        public string name, status;
        public bool npc;
        public long profit;
    }
    internal sealed class EliminationEvent
    {
        public int sequence, position, chamber;
        public string result;
        public long at;
    }
    internal sealed class EliminationView
    {
        public string id, phase;
        public int stake, turn, winner, emptyChambers;
        public long pool, deadline;
        public EliminationPlayer[] players;
        public EliminationEvent[] events;
    }
    internal sealed class TableSnapshot
    {
        public bool enabled, replayed;
        public long serverTime, balance, sequence;
        public int blackjackDecks;
        public int maxBuyIn = 5000, maxWager = 1000, economyVersion;
        public TableView table;
    }
    internal sealed class TableResponse
    {
        public string error, cursor;
        public bool retryable;
        public long serverTime;
        public TableSnapshot state;
    }
}
