using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Client;

var cases = new List<(string Name, Action Test)>();
void Test(string name, Action test) => cases.Add((name, test));
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
CasinoCommand Cmd(string op = "spin", long amount = 1, string c = "RUB", long seq = 1) => new(Guid.NewGuid().ToString("N"), seq, op, c, amount, "rippers-slot", ExchangeVersion: op is "buy" or "redeem" ? 3 : 0);
CasinoEngine Engine(params int[] values) => new(new CasinoConfig(), new Tickets(values.Length > 0 ? values : [0, 1, 3]));
var config = new CasinoConfig();
var rules = new SlotRules(config.Machine);
Test("default configuration valid", config.Validate);
Test("RTP exact weighted enumeration", () => Equal(26135d / 27000, rules.Statistics().Rtp));
Test("hit frequency exact enumeration", () => Equal(8100d / 27000, rules.Statistics().HitFrequency));
Test("30 weighted tickets map exactly", () => { for (int i = 0; i < 6; i++) Equal(config.Machine.Weights[i], Enumerable.Range(0, 30).Count(n => rules.Pick(n) == config.Machine.Symbols[i])); });
Test("negative RNG ticket rejected", () => Throws<ArgumentOutOfRangeException>(() => rules.Pick(-1)));
Test("upper RNG ticket rejected", () => Throws<ArgumentOutOfRangeException>(() => rules.Pick(30)));
for (int i = 0; i < 6; i++) { var n = i; Test("triple " + config.Machine.Symbols[n], () => Equal(config.Machine.Triples[n], rules.Multiplier(Enumerable.Repeat(config.Machine.Symbols[n], 3).ToArray()))); }
foreach (var o in new[] { new[] { "CHERRY", "CHERRY", "7" }, ["CHERRY", "BAR", "CHERRY"], ["LEMON", "CHERRY", "CHERRY"] }) Test("two cherries " + string.Join('/', o), () => Equal(2, rules.Multiplier(o)));
Test("one cherry loses", () => Equal(0, rules.Multiplier(["CHERRY", "BAR", "7"])));
Test("two sevens lose", () => Equal(0, rules.Multiplier(["7", "7", "LEMON"])));
Test("invalid symbol rejected", () => Throws<ArgumentException>(() => rules.Multiplier(["CHEAT", "7", "7"])));
Test("invalid reel count rejected", () => Throws<ArgumentException>(() => rules.Multiplier(["7", "7"])));
foreach (var bet in config.Machine.Bets) Test("valid losing bet " + bet, () => { var h = new Harness(1000); var r = h.Run(Cmd(amount: bet)); Equal(0L, r.Payout); Equal(1000L - bet, h.Inventory.Balance("CHIP")); });
Test("jackpot conservation physical chips", () => { var h = new Harness(100); var r = h.Run(Cmd(amount: 100), new Tickets([0])); Equal(10000L, r.Payout); Equal(10000L, h.Inventory.Balance("CHIP")); });
Test("insufficient physical chips", () => Throws<InvalidOperationException>(() => new Harness(0).Run(Cmd())));
Test("invalid bet rejected", () => Throws<ArgumentException>(() => new Harness(100).Run(Cmd(amount: 3))));
Test("unknown machine rejected", () => Throws<ArgumentException>(() => new Harness(100).Run(Cmd() with { Machine = "fake" })));
Test("unknown currency rejected", () => Throws<ArgumentException>(() => new Harness(100).Run(Cmd(c: "BTC"))));
Test("disabled casino", () => { var c = new CasinoConfig { Enabled = false }; Throws<InvalidOperationException>(() => new CasinoEngine(c, new Tickets([0])).Apply(new Wallet(), Cmd(), new Money(100))); });
Test("disabled redemption", () => { var c = new CasinoConfig { RedemptionEnabled = false }; Throws<InvalidOperationException>(() => new CasinoEngine(c, new Tickets([0])).Apply(new Wallet(), Cmd("redeem"), new Money(100))); });
Test("same spin replay consumes no RNG or items", () => { var h = new Harness(100); var q = Cmd(); h.Run(q, new Tickets([0])); h.Run(q, new NeverRandom()); Equal(199L, h.Inventory.Balance("CHIP")); Equal(2L, h.Wallet.NextSequence); });
Test("changed request payload rejected", () => { var h = new Harness(100); var q = Cmd(); h.Run(q); Throws<ArgumentException>(() => h.Run(q with { Amount = 5 })); });
Test("future sequence rejected", () => Throws<ArgumentException>(() => new Harness(100).Run(Cmd(seq: 2))));
Test("malformed request id rejected", () => Throws<ArgumentException>(() => new Harness(100).Run(Cmd() with { TransactionId = "x" })));
Test("older receipt rejected permanently", () => { var h = new Harness(100); var old = Cmd(); h.Run(old); h.Run(Cmd(seq: 2)); Throws<ArgumentException>(() => h.Run(old)); Equal(98L, h.Inventory.Balance("CHIP")); });
Test("reload preserves physical balance and receipt", () => { var h = new Harness(100); var q = Cmd(); h.Run(q, new Tickets([0])); h.Reload(); h.Run(q, new NeverRandom()); Equal(199L, h.Inventory.Balance("CHIP")); });
Test("concurrent duplicate model one wager", () => { var h = new Harness(100); var q = Cmd(); Parallel.For(0, 100, _ => { lock (h) h.Run(q); }); Equal(99L, h.Inventory.Balance("CHIP")); });
Test("concurrent different id at same sequence one wager", () => { var h = new Harness(100); int errors = 0; Parallel.For(0, 50, _ => { lock (h) { try { h.Run(Cmd()); } catch (ArgumentException) { errors++; } } }); Equal(49, errors); Equal(99L, h.Inventory.Balance("CHIP")); });
Test("rejected receipt deduplicates", () => { var w = new Wallet(); var q = Cmd(); CasinoEngine.Complete(w, q, new Receipt { Id = q.TransactionId, Fingerprint = q.Fingerprint, Error = "Insufficient" }); Equal(true, w.IsReplay(q)); });
Test("unsupported schema blocked", () => Throws<InvalidOperationException>(() => new Wallet { Version = 9 }.Validate()));
Test("zero weight blocked", () => { var c = new CasinoConfig(); c.Machine.Weights[0] = 0; Throws<InvalidOperationException>(c.Validate); });
Test("profitable slot blocked", () => { var c = new CasinoConfig(); c.Machine.Triples[4] = 100; Throws<InvalidOperationException>(c.Validate); });
Test("wallet cap before payout RNG", () => Throws<InvalidOperationException>(() => new Harness(config.MaxBalance).Run(Cmd(amount: 100), new NeverRandom())));
Test("full jackpot liability reserve before RNG", () => { var h = new Harness(100); h.Inventory.FailReserve = true; Throws<InvalidOperationException>(() => h.Run(Cmd(), new NeverRandom())); Equal(100L, h.Inventory.Balance("CHIP")); });
Test("unexpected payout add failure rolls back stake", () => { var h = new Harness(100); h.Inventory.FailCredit = "CHIP"; Throws<InvalidOperationException>(() => h.Run(Cmd(), new Tickets([0]))); Equal(100L, h.Inventory.Balance("CHIP")); Equal(1L, h.Wallet.NextSequence); });
Test("all weighted outcomes conserve physical chips", () => { for (int a = 0; a < 30; a++) for (int b = 0; b < 30; b++) for (int c = 0; c < 30; c++) { var inv = new Money(1000); var result = Engine(a, b, c).Apply(new Wallet(), Cmd(amount: 25), inv); Equal(975L + result.Payout, inv.Balance("CHIP")); } });
Test("old receipt version is unsupported without conversion", () => Throws<InvalidOperationException>(() => new Wallet { Version = 1 }.Validate()));
Test("already converted nonzero buckets fail safely", () => { var w = JsonSerializer.Deserialize<Wallet>("{\"Version\":2,\"Balances\":{\"RUB\":42}}")!; Throws<InvalidOperationException>(w.Validate); });
Test("presentation deducts stake immediately", () => { var p = new SpinPresentation(); p.Begin(320, 25); Equal(295L, p.VisibleBalance); Equal(0L, p.VisiblePayout); });
Test("resolved win remains concealed while reels move", () => { var p = new SpinPresentation(); p.Begin(320, 25); p.Resolve([4, 4, 0], 345, 50); foreach (var t in new[] { 0d, 1, SpinPresentation.FirstStop, SpinPresentation.SecondStop, (SpinPresentation.LastStop - .0001) }) { p.Tick(t); Equal(295L, p.VisibleBalance); Equal(0L, p.VisiblePayout); Equal(false, p.Revealed); } });
Test("last reel reveals payout exactly once", () => { var p = new SpinPresentation(); p.Begin(320, 25); p.Resolve([4, 4, 0], 345, 50); Equal(true, p.Tick(SpinPresentation.LastStop)); Equal(295L, p.VisibleBalance); Equal(50L, p.VisiblePayout); Equal(false, p.Tick(SpinPresentation.LastStop + SpinPresentation.CounterDuration * .5)); Equal(true, p.VisibleBalance > 295 && p.VisibleBalance < 345); p.Tick((SpinPresentation.LastStop + SpinPresentation.CounterDuration + .01)); Equal(345L, p.VisibleBalance); Equal(false, p.Active); });
Test("losing balance remains deducted", () => { var p = new SpinPresentation(); p.Begin(320, 25); p.Resolve([0, 1, 2], 295, 0); p.Tick(SpinPresentation.LastStop); Equal(295L, p.VisibleBalance); Equal(0L, p.VisiblePayout); });
Test("no receipt cannot reveal result", () => { var p = new SpinPresentation(); p.Begin(320, 25); Equal(false, p.Tick(100)); Equal(295L, p.VisibleBalance); });
Test("reels stop sequentially exactly on outcome", () => { var p = new SpinPresentation(); p.Begin(100, 1); p.Resolve([2, 4, 5], 99, 0); Equal(true, p.Stopped(0, SpinPresentation.FirstStop)); Equal(false, p.Stopped(1, SpinPresentation.FirstStop)); Equal(true, p.Stopped(1, SpinPresentation.SecondStop)); Equal(false, p.Stopped(2, SpinPresentation.SecondStop)); for (int r = 0; r < 3; r++) Equal((double)new[] { 2, 4, 5 }[r], p.Position(r, SpinPresentation.LastStop, 6) % 6); });
Test("reels accelerate and decelerate", () => { var p = new SpinPresentation(); p.Begin(100, 1); p.Resolve([0, 0, 0], 99, 0); var early = p.Position(0, .02, 6) - p.Position(0, .01, 6); var middle = p.Position(0, SpinPresentation.FirstStop * .5 + .01, 6) - p.Position(0, SpinPresentation.FirstStop * .5, 6); var late = p.Position(0, SpinPresentation.FirstStop - .01, 6) - p.Position(0, SpinPresentation.FirstStop - .02, 6); Equal(true, middle > early * 100); Equal(true, middle > late * 100); });
Test("double spin blocked during presentation", () => { var p = new SpinPresentation(); p.Begin(100, 1); Throws<InvalidOperationException>(() => p.Begin(99, 1)); });
Test("rejected spin restores authoritative display", () => { var p = new SpinPresentation(); p.Begin(100, 25); p.Reject(100); Equal(false, p.Active); Equal(100L, p.VisibleBalance); });
Test("hidden close reopen uses same presenter result", () => { var p = new SpinPresentation(); p.Begin(320, 25); p.Resolve([1, 1, 1], 1545, 1250); p.Tick(2); Equal(295L, p.VisibleBalance); p.Tick(SpinPresentation.LastStop); Equal(295L, p.VisibleBalance); p.Tick((SpinPresentation.LastStop + SpinPresentation.CounterDuration + .01)); Equal(1545L, p.VisibleBalance); });
Test("digital winnings need no free physical stash cell", () => { var w = new Wallet { Chips = 320 }; var native = new Money { FailReserve = true, FailCredit = "CHIP" }; var digital = new DigitalChipInventory(w, native, 10000); var result = Engine(0).Apply(w, Cmd(amount: 25), digital); Equal(2795L, w.Chips); Equal(2500L, result.Payout); Equal(0L, native.Balance("CHIP")); });
Test("digital payout counter remains active until exact final balance", () => { var p = new SpinPresentation(); p.Begin(320, 25); p.Resolve([4, 4, 0], 345, 50); p.Tick(SpinPresentation.LastStop); Equal(true, p.Active); long before = 295; for (double t = SpinPresentation.LastStop; t <= (SpinPresentation.LastStop + SpinPresentation.CounterDuration + .01); t += .02) { p.Tick(t); Equal(true, p.VisibleBalance >= before && p.VisibleBalance <= 345); before = p.VisibleBalance; } p.Tick((SpinPresentation.LastStop + SpinPresentation.CounterDuration + .01)); Equal(false, p.Active); Equal(345L, p.VisibleBalance); });
Test("next spin starts at exact previous unwrapped phase", () => { var p = new SpinPresentation(); p.SetRest([4, 2, 0]); p.Begin(100, 1); for (int r = 0; r < 3; r++) Equal((double)new[] { 4, 2, 0 }[r], p.Position(r, 0, 6)); p.Resolve([3, 1, 5], 99, 0, .83); p.Tick(5); var phases = Enumerable.Range(0, 3).Select(r => p.Position(r, 5, 6)).ToArray(); p.Begin(99, 1); for (int r = 0; r < 3; r++) Equal(phases[r], p.Position(r, 0, 6)); });
Test("receipt handoff preserves phase and velocity", () => { foreach (var wait in new[] { 0d, .01, .3, 1, 5, 28 }) { var p = new SpinPresentation(); p.SetRest([4, 2, 0]); p.Begin(100, 1); var before = Enumerable.Range(0, 3).Select(r => p.Position(r, wait, 6)).ToArray(); var velocity = (p.Position(0, wait + .00001, 6) - before[0]) / .00001; p.Resolve([2, 5, 4], 99, 0, wait); for (int r = 0; r < 3; r++) Equal(before[r], p.Position(r, 0, 6)); Equal(true, Math.Abs(velocity - (p.Position(0, .00001, 6) - before[0]) / .00001) < .002); } });
Test("monotone exact landings at low and high frame rates", () => { foreach (var fps in new[] { 5, 15, 30, 60, 144 }) foreach (var wait in new[] { 0d, .3, 2, 30 }) { var p = new SpinPresentation(); p.SetRest([5, 4, 3]); p.Begin(100, 1); p.Resolve([4, 0, 2], 99, 0, wait); for (int r = 0; r < 3; r++) { double last = p.Position(r, 0, 6); for (double t = 1d / fps; t < 4.8; t += 1d / fps) { var pos = p.Position(r, t, 6); Equal(true, pos + 1e-9 >= last); last = pos; } Equal((double)new[] { 4, 0, 2 }[r], last % 6); } } });
Test("reopening from receipt seeds the first visible spin", () => { var p = new SpinPresentation(); p.SetRest([4, 2, 0]); p.Begin(42, 1); for (int r = 0; r < 3; r++) Equal((double)new[] { 4, 2, 0 }[r], p.Position(r, 0, 6)); });
CasinoCommand Race(string selection = "killa", int stake = 1, long seq = 1) => new(Guid.NewGuid().ToString("N"), seq, "race", "RUB", stake, RaceConfig.MachineId, selection);
Test("racing defaults have stable names and descending favourite weights", () => { config.Racing.Validate(); Equal("killa,tagilla,gluhar,shturman,sanitar,reshala", string.Join(',', config.Racing.Racers.Select(r => r.Id))); Equal("2,3,5,7,9,11", string.Join(',', config.Racing.Racers.Select(r => r.ReturnMultiplier))); for (int i = 1; i < 6; i++) Equal(true, config.Racing.Racers[i - 1].Weight > config.Racing.Racers[i].Weight); });
Test("race quotes normalize into an exact exhaustive winner distribution", () => { var total = config.Racing.Racers.Sum(r => r.Weight); Equal(9551, total); foreach (var r in config.Racing.Racers) { Equal(r.Weight, Enumerable.Range(0, total).Count(t => config.Racing.Pick(t) == r.Id)); Equal(true, Math.Abs((double)r.Weight * r.ReturnMultiplier / total - 6930d / 9551) < 1e-12); } Throws<ArgumentOutOfRangeException>(() => config.Racing.Pick(-1)); Throws<ArgumentOutOfRangeException>(() => config.Racing.Pick(total)); });
foreach (var racer in config.Racing.Racers)
{
    var r = racer;
    Test("race win and stake-inclusive payout " + r.Id, () => { var w = new Wallet { Chips = 100 }; var inv = new DigitalChipInventory(w, new Money(), config.MaxBalance); var ticket = config.Racing.Racers.TakeWhile(x => x.Id != r.Id).Sum(x => x.Weight); var receipt = new CasinoEngine(config, new RaceTickets(ticket)).Apply(w, Race(r.Id, 5), inv); Equal(5L * r.ReturnMultiplier, receipt.Payout); Equal(95 + receipt.Payout, w.Chips); Equal(r.Id, receipt.Winner); Equal(r.Id, receipt.Selection); Equal(r.Id, receipt.FinishOrder[0]); Equal(6, receipt.FinishOrder.Distinct().Count()); Equal(receipt.Id, w.LastRace!.Id); });
}
Test("race losing selection only loses its stake", () => { var w = new Wallet { Chips = 100 }; var result = new CasinoEngine(config, new RaceTickets(0)).Apply(w, Race("reshala", 25), new DigitalChipInventory(w, new Money(), config.MaxBalance)); Equal("killa", result.Winner); Equal(0L, result.Payout); Equal(75L, w.Chips); });
Test("race replay after serialization changes no chips and consumes no random values", () => { var w = new Wallet { Chips = 100 }; var q = Race("tagilla", 5); var r = new CasinoEngine(config, new RaceTickets(3465)).Apply(w, q, new DigitalChipInventory(w, new Money(), config.MaxBalance)); w = JsonSerializer.Deserialize<Wallet>(JsonSerializer.Serialize(w))!; var replay = new CasinoEngine(config, new NeverRandom()).Apply(w, q, new DigitalChipInventory(w, new Money(), config.MaxBalance)); Equal(r.Id, replay.Id); Equal(110L, w.Chips); Equal(2L, w.NextSequence); Equal(r.Winner, w.LastRace!.Winner); Equal(string.Join(',', r.FinishOrder), string.Join(',', replay.FinishOrder)); });
Test("changing race selection on a replay fails", () => { var w = new Wallet { Chips = 100 }; var q = Race(); new CasinoEngine(config, new RaceTickets(0)).Apply(w, q, new DigitalChipInventory(w, new Money(), config.MaxBalance)); Throws<ArgumentException>(() => new CasinoEngine(config, new NeverRandom()).Apply(w, q with { Selection = "tagilla" }, new DigitalChipInventory(w, new Money(), config.MaxBalance))); });
Test("invalid race, stake and funds fail before RNG", () => { foreach (var q in new[] { Race("unknown"), Race(stake: 3), Race() with { Machine = "rippers-slot" } }) { var w = new Wallet { Chips = 100 }; Throws<ArgumentException>(() => new CasinoEngine(config, new NeverRandom()).Apply(w, q, new DigitalChipInventory(w, new Money(), config.MaxBalance))); Equal(100L, w.Chips); Equal(1L, w.NextSequence); } var empty = new Wallet(); Throws<InvalidOperationException>(() => new CasinoEngine(config, new NeverRandom()).Apply(empty, Race(), new DigitalChipInventory(empty, new Money(), config.MaxBalance))); });
Test("race reserves wallet liability before RNG", () => { var w = new Wallet { Chips = config.MaxBalance }; Throws<InvalidOperationException>(() => new CasinoEngine(config, new NeverRandom()).Apply(w, Race("reshala", 100), new DigitalChipInventory(w, new Money(), config.MaxBalance))); Equal(config.MaxBalance, w.Chips); });
Test("race payouts need no physical stash space", () => { var w = new Wallet { Chips = 100 }; var native = new Money { FailCredit = "CHIP", FailReserve = true }; new CasinoEngine(config, new RaceTickets(0)).Apply(w, Race("killa", 5), new DigitalChipInventory(w, native, config.MaxBalance)); Equal(105L, w.Chips); Equal(0L, native.Balance("CHIP")); });
Test("disabled or profitable racing configuration is blocked", () => { var c = new CasinoConfig(); c.Racing.Enabled = false; var w = new Wallet { Chips = 100 }; Throws<InvalidOperationException>(() => new CasinoEngine(c, new NeverRandom()).Apply(w, Race(), new DigitalChipInventory(w, new Money(), c.MaxBalance))); c = new CasinoConfig(); c.Racing.Racers[0].ReturnMultiplier = 10; Throws<InvalidOperationException>(c.Validate); c = new CasinoConfig(); c.Racing.Racers[0].Weight = 0; Throws<InvalidOperationException>(c.Validate); });
Test("current slot fingerprint includes economy v4", () => { var q = Cmd(); Equal($"economy:4:{q.Sequence}:spin:RUB:1:rippers-slot", q.Fingerprint); });
Test("last slot survives a race and cashier transaction", () => { var w = new Wallet { Chips = 100 }; var inv = new DigitalChipInventory(w, new Money(), config.MaxBalance); var slot = Engine(29).Apply(w, Cmd(), inv); var race = new CasinoEngine(config, new RaceTickets(0)).Apply(w, Race(seq: 2), inv); Engine().Apply(w, Cmd("buy", 1, seq: 3), inv); Equal(slot.Id, w.LastSlot!.Id); Equal(race.Id, w.LastRace!.Id); Equal("buy", w.Last!.Operation); });
Test("race stake and payout remain concealed until finish", () => { var p = new RacePresentation(); p.Begin(100, 5); Equal(95L, p.VisibleBalance); Equal(false, p.Tick(100)); p.Resolve([2, 0, 5, 1, 4, 3], 120, 25); foreach (var t in new[] { 0d, 2, RacePresentation.FirstFinish, RacePresentation.FirstFinish + 5 * RacePresentation.FinishGap, RacePresentation.FinishTime - .001 }) { Equal(false, p.Tick(t)); Equal(95L, p.VisibleBalance); Equal(0L, p.VisiblePayout); } Equal(true, p.Tick(RacePresentation.FinishTime)); Equal(25L, p.VisiblePayout); Equal(95L, p.VisibleBalance); Equal(false, p.Tick(RacePresentation.FinishTime + .1)); p.Tick(RacePresentation.FinishTime + RacePresentation.CounterDuration + .01); Equal(120L, p.VisibleBalance); Equal(false, p.Active); });
Test("race progresses forward with authoritative order at multiple FPS", () => { foreach (var fps in new[] { 5, 15, 30, 60, 144 }) { var p = new RacePresentation(); p.Begin(100, 1); p.Resolve([3, 0, 5, 1, 4, 2], 99, 0); for (int racer = 0; racer < 6; racer++) { double previous = 0; for (double t = 0; t < 9; t += 1d / fps) { var current = p.Position(racer, t); Equal(true, current >= previous - 1e-10 && current >= 0 && current <= 1 + 1e-10); previous = current; } Equal(1d, p.Position(racer, 9)); } Equal(1d, p.Position(3, RacePresentation.FirstFinish)); Equal(true, p.Position(0, RacePresentation.FirstFinish) < 1); Equal(true, p.Position(2, (RacePresentation.FirstFinish + 5 * RacePresentation.FinishGap - .01)) < 1); } });
Test("race close and reopen retains same presenter through payout counter", () => { var p = new RacePresentation(); p.Begin(100, 5); p.Resolve([0, 1, 2, 3, 4, 5], 105, 10); p.Tick(2); var pos = p.Position(0, 2); Equal(pos, p.Position(0, 2)); p.Tick(RacePresentation.FinishTime); long previous = 95; for (double t = RacePresentation.FinishTime; t < RacePresentation.FinishTime + RacePresentation.CounterDuration + .01; t += .01) { p.Tick(t); Equal(true, p.VisibleBalance >= previous && p.VisibleBalance <= 105); previous = p.VisibleBalance; } p.Tick(RacePresentation.FinishTime + RacePresentation.CounterDuration + .01); Equal(105L, p.VisibleBalance); Equal(false, p.Active); });
Test("malformed race finish is rejected without revealing funds", () => { var p = new RacePresentation(); p.Begin(100, 5); Throws<InvalidOperationException>(() => p.Resolve([0, 0, 2, 3, 4, 5], 105, 10)); Equal(false, p.Resolved); Throws<InvalidOperationException>(() => p.Resolve([0, 1, 2, 3, 4, 5], 106, 10)); Equal(false, p.Resolved); Equal(95L, p.VisibleBalance); });
Test("polished pacing is 25 percent shorter for slots and 45 percent shorter for races", () => { Equal(true, Math.Abs((SpinPresentation.LastStop + SpinPresentation.CounterDuration) / (3.7 + .85) - .75) < 1e-12); Equal(true, Math.Abs((RacePresentation.FinishTime + RacePresentation.CounterDuration) / (8 + .85) - .55) < 1e-12); Equal(true, RacePresentation.FirstFinish + 5 * RacePresentation.FinishGap < RacePresentation.FinishTime); });
Test("wallet JSON preserves its schema and unknown saved fields", () =>
{
    const string saved = """
        {"Version":4,"Chips":42,"NextSequence":8,"LastRequestId":"old-receipt","FutureData":{"value":17}}
        """;
    var wallet = JsonSerializer.Deserialize<Wallet>(saved)!;
    wallet.Validate();
    using var document = JsonDocument.Parse(JsonSerializer.Serialize(wallet));
    var root = document.RootElement;
    Equal(4, root.GetProperty("Version").GetInt32());
    Equal(42L, root.GetProperty("Chips").GetInt64());
    Equal(8L, root.GetProperty("NextSequence").GetInt64());
    Equal("old-receipt", root.GetProperty("LastRequestId").GetString());
    Equal(17, root.GetProperty("FutureData").GetProperty("value").GetInt32());
});
CashierTests.Register(Test);
PresentationTests.Register(Test);
        PublicReleaseTests.Register(Test);
TableTests.Register(Test);
BlackjackTests.Register(Test);
PokerTests.Register(Test);
RouletteTests.Register(Test);
EliminationTests.Register(Test);
int failed = 0;
var selectedCases = (args.Contains("--economy-only") || args.Contains("--cashier-only")) ? cases.Where(c => c.Name.StartsWith("economy / ")).ToList() : args.Contains("--post-release-only") ? cases.Where(c => c.Name.StartsWith("post-release / ") || c.Name.StartsWith("blackjack ") || c.Name.Contains("AUTO", StringComparison.Ordinal) || c.Name.Contains("Blackjack pays exact 3:2", StringComparison.Ordinal)).ToList() : args.Contains("--polish-only") ? cases.Where(c => c.Name.StartsWith("polish / ") || c.Name.StartsWith("economy / russian-roulette") || c.Name.StartsWith("fictional elimination ") || c.Name.Contains("AUTO", StringComparison.Ordinal)).ToList() : args.Contains("--revolver-only") ? cases.Where(c => c.Name.Contains("revolver", StringComparison.OrdinalIgnoreCase)).ToList() : cases;
foreach (var (name, test) in selectedCases) { try { test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
Console.WriteLine($"{selectedCases.Count - failed}/{selectedCases.Count} passed");
return failed == 0 ? 0 : 1;
sealed class Tickets(int[] tickets) : IRandomSource
{
    private int n; public int Next(int max) => tickets[n++ % tickets.Length];
}
sealed class RaceTickets(int first) : IRandomSource
{
    private bool used; public int Next(int max)
    {
        if (!used)
        {
            used = true;
            return first;
        }
        return 0;
    }
}
sealed class NeverRandom : IRandomSource
{
    public int Next(int max) => throw new Exception("RNG consumed before safe preflight or during replay.");
}
sealed class Money : ICurrencyInventory
{
    public Dictionary<string, long> Units { get; set; } = new() { { "RUB", 100000 }, { "USD", 100000 }, { "EUR", 100000 }, { "CHIP", 0 } };
    public string? FailCredit
    {
        get; set;
    }
    public bool FailReserve
    {
        get; set;
    }
    public Money() : this(0) { }
    public Money(long chips)
    {
        Units["CHIP"] = chips;
    }
    public long Balance(string c) => Units.GetValueOrDefault(c);
    public void Debit(string c, long n)
    {
        if (n < 0 || Units[c] < n)
            throw new InvalidOperationException("Insufficient items");
        Units[c] -= n;
    }
    public void Credit(string c, long n)
    {
        if (FailCredit == c)
            throw new InvalidOperationException("No space");
        Units[c] = checked(Units[c] + n);
    }
    public void ReservePayout(string c, long d, long p)
    {
        if (FailReserve)
            throw new InvalidOperationException("No space for maximum payout");
    }
}
sealed class Harness(long chips)
{
    public Wallet Wallet { get; private set; } = new(); public Money Inventory { get; private set; } = new(chips);
    public Receipt Run(CasinoCommand q, IRandomSource? random = null)
    {
        var stagedWallet = JsonSerializer.Deserialize<Wallet>(JsonSerializer.Serialize(Wallet))!;
        var stagedInv = JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(Inventory))!;
        var r = new CasinoEngine(new CasinoConfig(), random ?? new Tickets([0, 1, 3])).Apply(stagedWallet, q, stagedInv);
        Wallet = stagedWallet;
        Inventory = stagedInv;
        return r;
    }
    public void Reload()
    {
        Wallet = JsonSerializer.Deserialize<Wallet>(JsonSerializer.Serialize(Wallet))!;
        Inventory = JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(Inventory))!;
    }
}

