using HoldemPoker.Cards;
using HoldemPoker.Evaluator;

namespace RippersCasino.Core.Games;

public sealed class PokerPlayer
{
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Style { get; set; } = "tight";
    public bool Npc { get; set; }
    public long Bankroll { get; set; }
    public long Stack { get; set; }
    public long StreetBet { get; set; }
    public long TotalBet { get; set; }
    public bool Folded { get; set; }
    public bool Acted { get; set; }
    public long ActedAtBet { get; set; }
    public bool Leave { get; set; }
    public int[] Hole { get; set; } = [];
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
}
public sealed class PokerRound
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phase { get; set; } = "playing";
    public string Street { get; set; } = "preflop";
    public int[] Deck { get; set; } = [];
    public int Drawn { get; set; }
    public List<int> Burned { get; set; } = [];
    public List<int> Board { get; set; } = [];
    public List<PokerPlayer> Players { get; set; } = [];
    public List<string> Events { get; set; } = [];
    public int Button { get; set; }
    public int Turn { get; set; }
    public int BigBlind { get; set; }
    public long CurrentBet { get; set; }
    public long LastRaise { get; set; }
    public long Deadline { get; set; }
    public bool Showdown { get; set; }
}
public sealed record PokerPlayerView(int Position, string Name, string Style, bool Npc, long Stack, long Bet, long Committed,
    int[] Cards, bool Folded, bool AllIn, string Action, string Result, long Profit);
public sealed record PokerView(string Id, string Phase, string Street, int Button, int Turn, int BigBlind, long Deadline,
    int[] Board, long Pot, long Call, long MinRaise, long MaxRaise, bool CanRaise, string[] Events, PokerPlayerView[] Players);

public static class Poker
{
    private static Card ConvertCard(int card) => Card.Parse("A23456789TJQK"[card % 13].ToString() + "cdhs"[card / 13]);
    public static int Rank(IEnumerable<int> cards) => HoldemHandEvaluator.GetHandRanking(cards.Select(ConvertCard).ToArray());
    public static string Description(IEnumerable<int> cards) => HoldemHandEvaluator.GetHandDescription(Rank(cards));
    private static PokerPlayer Next(PokerRound r, int after, Func<PokerPlayer, bool> predicate) => r.Players
        .Where(predicate).OrderBy(p => (p.Position - after + 6) % 6 == 0 ? 6 : (p.Position - after + 6) % 6).First();
    private static bool Able(PokerPlayer p) => !p.Folded && p.Stack > 0;
    private static int Draw(PokerRound r) => r.Deck[r.Drawn++];
    private static void Log(PokerRound r, string text)
    {
        r.Events.Add(text);
        if (r.Events.Count > 8)
            r.Events.RemoveAt(0);
    }
    private static void Pay(PokerPlayer p, long amount)
    {
        p.Stack -= amount;
        p.StreetBet += amount;
        p.TotalBet += amount;
    }
    public static bool MayRaise(PokerRound r, PokerPlayer p) => p.Stack + p.StreetBet > r.CurrentBet &&
        (!p.Acted || p.ActedAtBet == 0 || r.CurrentBet - p.ActedAtBet >= r.LastRaise);
    public static long MinimumRaise(PokerRound r) => r.CurrentBet < r.BigBlind ? r.BigBlind : r.CurrentBet + r.LastRaise;
    public static PokerRound Deal(List<PokerPlayer> players, IRandomSource random, int bigBlind, int button, long now, int seconds)
    {
        if (players.Count is < 2 or > 6 || bigBlind < 2 || bigBlind % 2 != 0 || players.Any(p => p.Bankroll < 1))
            throw new InvalidOperationException("Choose an even big blind and a funded table.");
        var r = new PokerRound { Deck = Cards.Shuffle(random), Players = players.OrderBy(p => p.Position).ToList(), BigBlind = bigBlind, Button = button, CurrentBet = bigBlind, LastRaise = bigBlind };
        foreach (var p in r.Players)
        {
            p.Stack = p.Bankroll;
            p.Hole = [Draw(r), Draw(r)];
        }
        var small = r.Players.Count == 2 ? r.Players.Single(p => p.Position == button) : Next(r, button, _ => true);
        var big = Next(r, small.Position, _ => true);
        Pay(small, Math.Min(small.Stack, bigBlind / 2));
        small.Action = "SMALL BLIND";
        Pay(big, Math.Min(big.Stack, bigBlind));
        big.Action = "BIG BLIND";
        r.Turn = Next(r, big.Position, _ => true).Position;
        Log(r, "Blinds " + bigBlind / 2 + " / " + bigBlind);
        Continue(r, now, seconds, random);
        return r;
    }
    public static void Act(PokerRound r, int position, string action, long amount, long now, int seconds, IRandomSource random)
    {
        if (r.Phase != "playing" || r.Turn != position)
            throw new InvalidOperationException("Wait for your turn.");
        var p = r.Players.Single(p => p.Position == position);
        Apply(r, p, action, amount);
        r.Turn = Next(r, position, _ => true).Position;
        Continue(r, now, seconds, random);
    }
    private static void Apply(PokerRound r, PokerPlayer p, string action, long amount)
    {
        if (!Able(p))
            throw new InvalidOperationException("This hand is no longer in play.");
        var call = Math.Max(0, r.CurrentBet - p.StreetBet);
        if (action == "all-in")
        {
            amount = p.Stack + p.StreetBet;
            action = amount <= r.CurrentBet ? "call" : "raise";
        }
        switch (action)
        {
            case "fold":
                p.Folded = true;
                p.Action = "FOLD";
                break;
            case "check":
                if (call != 0)
                    throw new InvalidOperationException("Call or fold when facing a bet.");
                p.Action = "CHECK";
                break;
            case "call":
                if (call == 0)
                    throw new InvalidOperationException("There is no bet to call.");
                var paid = Math.Min(call, p.Stack);
                Pay(p, paid);
                p.Action = "CALL " + paid;
                break;
            case "raise":
                var max = p.Stack + p.StreetBet;
                if (!MayRaise(r, p) || amount <= r.CurrentBet || amount > max || amount < MinimumRaise(r) && amount != max)
                    throw new InvalidOperationException("Choose a legal raise. A short all-in may not allow another raise.");
                var increment = amount - r.CurrentBet;
                Pay(p, amount - p.StreetBet);
                if (increment >= r.LastRaise)
                    r.LastRaise = increment;
                r.CurrentBet = amount;
                p.Action = "RAISE TO " + amount;
                break;
            default:
                throw new ArgumentException("Unknown poker decision.");
        }
        p.Acted = true;
        p.ActedAtBet = r.CurrentBet;
        if (p.Stack == 0 && !p.Folded)
            p.Action += " / ALL-IN";
        Log(r, p.Name + ": " + p.Action);
    }
    private static bool NeedsAction(PokerRound r, PokerPlayer p) => Able(p) && (!p.Acted || p.StreetBet < r.CurrentBet);
    private static void Continue(PokerRound r, long now, int seconds, IRandomSource random)
    {
        // Bounded by four streets, each NPC's finite stack and maximum six seats.
        for (var guard = 0; guard < 4096; guard++)
        {
            var alive = r.Players.Where(p => !p.Folded).ToArray();
            if (alive.Length == 1)
            {
                Settle(r);
                return;
            }
            var able = alive.Where(Able).ToArray();
            var needs = r.Players.Where(p => NeedsAction(r, p)).ToArray();
            if (needs.Length == 0 || able.Length <= 1 && able.All(p => p.StreetBet >= alive.Max(q => q.StreetBet)))
            {
                if (r.Street == "river")
                {
                    r.Showdown = true;
                    Settle(r);
                    return;
                }
                r.Burned.Add(Draw(r));
                var count = r.Street == "preflop" ? 3 : 1;
                for (var i = 0; i < count; i++)
                    r.Board.Add(Draw(r));
                r.Street = r.Board.Count == 3 ? "flop" : r.Board.Count == 4 ? "turn" : "river";
                r.CurrentBet = 0;
                r.LastRaise = r.BigBlind;
                foreach (var p in r.Players)
                {
                    p.StreetBet = 0;
                    p.Acted = false;
                    p.ActedAtBet = 0;
                }
                r.Turn = Next(r, r.Button, _ => true).Position;
                Log(r, r.Street.ToUpperInvariant());
                continue;
            }
            var current = r.Players.Single(p => p.Position == r.Turn);
            if (!NeedsAction(r, current))
            {
                r.Turn = Next(r, current.Position, _ => true).Position;
                continue;
            }
            if (!current.Npc && !current.Leave)
            {
                r.Deadline = checked(now + seconds);
                return;
            }
            var decision = current.Leave ? ("fold", 0L) : NpcDecision(current.Hole, r.Board.ToArray(), current.Style,
                current.Stack, current.StreetBet, r.CurrentBet, r.LastRaise, r.BigBlind, r.Players.Sum(p => p.TotalBet), MayRaise(r, current), random);
            Apply(r, current, decision.Item1, decision.Item2);
            r.Turn = Next(r, current.Position, _ => true).Position;
        }
        throw new InvalidOperationException("This hand could not continue. Close the casino and check the log.");
    }
    // Only this NPC's cards and public betting information cross this boundary.
    public static (string, long) NpcDecision(int[] hole, int[] board, string style, long stack, long bet, long current,
        long lastRaise, int blind, long pot, bool canRaise, IRandomSource random)
    {
        var ranks = hole.Select(c => c % 13 == 0 ? 14 : c % 13 + 1).ToArray();
        var pair = ranks[0] == ranks[1];
        var strength = board.Length >= 3 ? 1 - Rank(hole.Concat(board)) / 7463.0 :
            Math.Clamp((ranks.Sum() - 6) / 28.0 + (pair ? .28 : 0) + (hole[0] / 13 == hole[1] / 13 ? .07 : 0), 0, 1);
        var call = Math.Max(0, current - bet);
        var tolerance = style == "loose" ? .16 : style == "aggressive" ? .08 : style == "tight" ? -.10 : 0;
        if (call > 0 && strength + tolerance < .28 + (double)call / Math.Max(1, pot + call) * .4)
            return ("fold", 0);
        var raise = Math.Max(current < blind ? blind : current + lastRaise, current + Math.Max(blind, pot / 2));
        if (canRaise && raise <= stack + bet && (strength > .78 || style == "aggressive" && random.Next(10) == 0))
            return ("raise", raise);
        return call == 0 ? ("check", 0) : ("call", 0);
    }
    public static void Settle(PokerRound r)
    {
        if (r.Phase == "complete")
            return;
        var active = r.Players.Where(p => !p.Folded).ToArray();
        // Return a unique unmatched overbet, including an unmatched blind.
        var ordered = r.Players.OrderByDescending(p => p.TotalBet).ToArray();
        if (ordered[0].TotalBet > ordered[1].TotalBet)
        {
            var refund = ordered[0].TotalBet - ordered[1].TotalBet;
            ordered[0].TotalBet -= refund;
            ordered[0].Stack += refund;
            ordered[0].StreetBet = Math.Min(ordered[0].StreetBet, ordered[0].TotalBet);
        }
        var previous = 0L;
        foreach (var level in r.Players.Select(p => p.TotalBet).Where(v => v > 0).Distinct().Order())
        {
            var contributors = r.Players.Where(p => p.TotalBet >= level).ToArray();
            var pot = checked((level - previous) * contributors.Length);
            previous = level;
            var eligible = contributors.Where(p => !p.Folded).ToArray();
            if (eligible.Length == 0)
                throw new InvalidDataException("The pot could not be settled. Close the casino and check the log.");
            var best = eligible.Length == 1 ? 0 : eligible.Min(p => Rank(p.Hole.Concat(r.Board)));
            var winners = eligible.Where(p => eligible.Length == 1 || Rank(p.Hole.Concat(r.Board)) == best)
                .OrderBy(p => (p.Position - r.Button + 6) % 6 == 0 ? 6 : (p.Position - r.Button + 6) % 6).ToArray();
            for (var i = 0; i < winners.Length; i++)
                winners[i].Stack += pot / winners.Length + (i < pot % winners.Length ? 1 : 0);
        }
        if (r.Players.Sum(p => p.Stack) != r.Players.Sum(p => p.Bankroll))
            throw new InvalidDataException("The pot could not be settled. Close the casino and check the log.");
        foreach (var p in r.Players)
            p.Result = p.Folded ? "FOLDED" : r.Showdown ? Description(p.Hole.Concat(r.Board)) : "UNCONTESTED";
        r.Phase = "complete";
        r.Deadline = 0;
        Log(r, "Hand settled / no house fee");
    }
    public static PokerView View(PokerRound r, int viewer)
    {
        var p = r.Players.SingleOrDefault(p => p.Position == viewer);
        return new(r.Id, r.Phase, r.Street, r.Button, r.Turn, r.BigBlind, r.Deadline, r.Board.ToArray(), r.Players.Sum(p => p.TotalBet),
            p == null ? 0 : Math.Min(p.Stack, Math.Max(0, r.CurrentBet - p.StreetBet)), MinimumRaise(r), p == null ? 0 : p.Stack + p.StreetBet,
            p != null && MayRaise(r, p), r.Events.ToArray(), r.Players.Select(q => new PokerPlayerView(q.Position, q.Name, q.Style, q.Npc, q.Stack,
                q.StreetBet, q.TotalBet, q.Position == viewer || r.Phase == "complete" && r.Showdown && !q.Folded ? q.Hole.ToArray() : [-1, -1],
                q.Folded, r.Phase == "playing" && !q.Folded && q.Stack == 0, q.Action, q.Result, r.Phase == "complete" ? q.Stack - q.Bankroll : 0)).ToArray());
    }
    public static void Validate(PokerRound r)
    {
        if (!Guid.TryParseExact(r.Id, "N", out _) || r.Deck.Length != 52 || r.Deck.Order().Where((c, i) => c != i).Any() ||
            r.Players.Count is < 2 or > 6 || r.Players.Select(p => p.Position).Distinct().Count() != r.Players.Count ||
            r.Players.Any(p => p.Position is < 0 or > 5 || p.Hole.Length != 2 || p.Stack < 0 || p.Bankroll < 1 || p.TotalBet < 0 || p.StreetBet < 0 || p.StreetBet > p.TotalBet) ||
            !r.Players.Any(p => p.Position == r.Button) || !r.Players.Any(p => p.Position == r.Turn) || r.BigBlind < 2 || r.BigBlind % 2 != 0 ||
            r.Phase is not ("playing" or "complete") || r.Street is not ("preflop" or "flop" or "turn" or "river") || r.CurrentBet < 0 || r.LastRaise < r.BigBlind)
            throw new InvalidDataException("Your saved hand could not be restored. Close the casino and check the log.");
        var dealt = r.Players.SelectMany(p => p.Hole).Concat(r.Board).Concat(r.Burned).Order().ToArray();
        if (r.Drawn != dealt.Length || !dealt.SequenceEqual(r.Deck.Take(r.Drawn).Order()) ||
            r.Board.Count != (r.Street == "preflop" ? 0 : r.Street == "flop" ? 3 : r.Street == "turn" ? 4 : 5) ||
            r.Phase == "playing" && (r.Players.Sum(p => p.Stack + p.TotalBet) != r.Players.Sum(p => p.Bankroll) || !Able(r.Players.Single(p => p.Position == r.Turn)) || r.Deadline <= 0) ||
            r.Phase == "complete" && r.Players.Sum(p => p.Stack) != r.Players.Sum(p => p.Bankroll))
            throw new InvalidDataException("Your saved hand could not be restored. Close the casino and check the log.");
    }
}
