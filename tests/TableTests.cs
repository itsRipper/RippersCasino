using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Core.Tables;
using RippersCasino.Core.Games;

internal static class TableTests
{
    public static void Register(Action<string, Action> test)
    {
        void Case(string name, Func<Fixture, Task> run) => test(name, () => { using var fixture = new Fixture(); run(fixture).GetAwaiter().GetResult(); });
        foreach (var game in new[] { "blackjack", "poker", "roulette", "russian-roulette" })
        {
            Case("economy / " + game + " enforces the wager limit in whole chips", async f =>
            {
                f.Coordinator = new(f.Money, f.Storage, new TableOptions { MaxWager = 2 }, () => f.Now, f.Random);
                var created = await f.Coordinator.ActAsync("alice", f.Intent("solo", game: game, amount: game == "russian-roulette" ? 100 : 20));
                if (game == "russian-roulette")
                {
                    try { await f.Act("start", created.Table!, 101); throw new Exception("Bankroll limit was ignored"); }
                    catch (InvalidOperationException) { }
                    var accepted = await f.Act("start", created.Table!, 100);
                    Require(accepted.Table!.Elimination!.Stake == 100 && accepted.Table.Elimination.Pool == 200 && f.Storage.Load().Tables.Single().Elimination!.Players.Single(p => p.Npc).Bankroll == 100);
                }
                else if (game == "roulette")
                {
                    var open = await f.Act("start", created.Table!);
                    try { await f.Act("bet", open.Table!, 3, "number:0"); throw new Exception("Wager limit was ignored"); }
                    catch (InvalidOperationException) { }
                    var accepted = await f.Act("bet", open.Table!, 2, "number:0");
                    Require(accepted.Table!.Roulette!.Bets.Single().Stake == 2);
                }
                else
                {
                    try { await f.Act("start", created.Table!, 4); throw new Exception("Wager limit was ignored"); }
                    catch (InvalidOperationException) { }
                    var accepted = await f.Act("start", created.Table!, 2);
                    Require(accepted.Table != null && accepted.Balance == 80);
                }
            });
            Case(game + " is independently playable against NPCs or the house", async f =>
            {
                var created = await f.Create(game);
                Require(created.Table!.Seats.Length == 1 && created.Balance == 80);
                var start = f.Intent("start", created.Table, amount: game is "blackjack" or "poker" ? 2 : 1);
                var state = await f.Coordinator.ActAsync("alice", start);
                Require((await f.Coordinator.ActAsync("alice", start)).Replayed);
                if (game == "blackjack") Require(state.Table!.Blackjack!.Hands.Count(h => h.Npc) is >= 1 and <= 2 && state.Table.Blackjack.Hands.Length <= 3);
                if (game == "poker") Require(state.Table!.Poker!.Players.Count(p => p.Npc) is >= 1 and <= 4);
                if (game == "russian-roulette") Require(state.Table!.Elimination!.Players.Length == 2 && state.Table.Elimination.Players.Count(p => p.Npc) == 1);
                if (game == "roulette") state = await f.Act("bet", state.Table!, 1, "number:0");
                for (var turn = 0; state.Table!.Phase == "playing" && turn < 128; turn++)
                {
                    var table = state.Table;
                    if (game == "blackjack") state = await f.Act("stand", table);
                    else if (game == "poker")
                    {
                        var own = table.Poker!.Players.Single(p => !p.Npc);
                        if (table.Poker.Turn == own.Position) state = await f.Act("all-in", table);
                    }
                    else if (game == "russian-roulette" && table.Elimination!.Turn == table.Seats[0].Position) state = await f.Act("take-turn", table);
                    f.Now += 50;
                    state = await f.Coordinator.PollAsync("alice");
                }
                Require(state.Table!.Phase == "waiting" && f.Money.Read("alice").Liability == 0);
                var held = f.Money.Read("alice").Held;
                var leave = f.Intent("leave", state.Table);
                await f.Coordinator.ActAsync("alice", leave);
                f.Restart();
                Require((await f.Coordinator.ActAsync("alice", leave)).Replayed);
                Require(f.Money.Read("alice").Chips == 80 + held && f.Money.Read("alice").Held == 0);
            });
        }
        Case("closed tables compact while archived creation and cashout replays remain safe", async f =>
        {
            var first = f.Intent("solo", game: "poker", amount: 20);
            var created = await f.Coordinator.ActAsync("alice", first);
            var leave = f.Intent("leave", created.Table);
            await f.Coordinator.ActAsync("alice", leave);
            for (var i = 0; i < 80; i++)
            {
                var game = await f.Create("poker");
                await f.Act("leave", game.Table!);
            }
            var saved = f.Storage.Load();
            Require(saved.Tables.Count == 0 && saved.Actions.Count == TableCoordinator.RecentActionLimit);
            f.Restart();
            Require((await f.Coordinator.ActAsync("alice", first)).Replayed);
            Require((await f.Coordinator.ActAsync("alice", leave)).Replayed);
            await Fails<ArgumentException>(() => f.Coordinator.ActAsync("alice", first with { Amount = 21 }));
            Require(f.Money.Read("alice").Chips == 100 && f.Money.Read("alice").Held == 0);
        });
        Case("compacted receipts survive a failed wallet commit and recovery", async f =>
        {
            var first = f.Intent("solo", game: "poker", amount: 20);
            var a = await f.Coordinator.ActAsync("alice", first);
            await f.Act("leave", a.Table!);
            for (var i = 0; i < 63; i++) { a = await f.Create("poker"); await f.Act("leave", a.Table!); }
            f.Money.FailPlayer = "alice";
            var pending = f.Intent("solo", game: "blackjack", amount: 20);
            await Fails<IOException>(() => f.Coordinator.ActAsync("alice", pending));
            f.Money.FailPlayer = null; f.Restart(); await f.Coordinator.RecoverAsync();
            Require((await f.Coordinator.ActAsync("alice", pending)).Replayed);
            Require((await f.Coordinator.ActAsync("alice", first)).Replayed);
            Require(f.Money.Read("alice").Chips == 80 && f.Money.Read("alice").Held == 20);
        });
        Case("conflicting archive evidence blocks compaction before another wager", async f =>
        {
            var first = f.Intent("solo", game: "poker", amount: 20);
            var a = await f.Coordinator.ActAsync("alice", first);
            var receipt = f.Storage.Load().Actions.Single();
            f.Storage.Archive(receipt with { Fingerprint = "conflicting evidence" });
            await f.Act("leave", a.Table!);
            for (var i = 0; i < 63; i++) { a = await f.Create("poker"); await f.Act("leave", a.Table!); }
            var before = f.Storage.Load().Revision;
            await Fails<InvalidDataException>(() => f.Create("poker"));
            Require(f.Storage.Load().Revision == before && !f.Coordinator.RecoveryPending);
            Require(f.Money.Read("alice").Chips == 100 && f.Money.Read("alice").Held == 0);
            Require((await f.Coordinator.ActAsync("alice", first)).Replayed);
        });
        Case("human admission and readiness actions are absent", async f =>
        {
            foreach (var op in new[] { "create", "join", "ready", "unready" }) await Fails<ArgumentException>(() => f.Coordinator.ActAsync("alice", f.Intent(op)));
            Require(f.Money.Read("alice").Chips == 100 && (await f.Coordinator.PollAsync("alice")).Table == null);
        });
        Case("profiles own separate games without table discovery", async f =>
        {
            var a = await f.Create("poker");
            var b = await f.Coordinator.ActAsync("bob", f.Intent("solo", game: "blackjack", amount: 20));
            Require(a.Table!.Id != b.Table!.Id && b.Table.Seats.Length == 1);
            await Fails<InvalidOperationException>(() => f.Coordinator.ActAsync("bob", f.Intent("leave", a.Table)));
        });
        Case("one profile cannot double-reserve a bankroll", async f =>
        {
            await f.Create("poker"); await Fails<InvalidOperationException>(() => f.Create("poker"));
            Require(f.Money.Read("alice").Chips == 80 && f.Money.Read("alice").Held == 20);
        });
        Case("solo reservation replay survives restart", async f =>
        {
            var command = f.Intent("solo", game: "poker", amount: 20);
            await f.Coordinator.ActAsync("alice", command); f.Restart();
            Require((await f.Coordinator.ActAsync("alice", command)).Replayed && f.Money.Read("alice").Chips == 80);
            await Fails<ArgumentException>(() => f.Coordinator.ActAsync("alice", command with { Amount = 21 }));
        });
        Case("stale actions cannot change a solo game", async f =>
        {
            var a = await f.Create("blackjack");
            await f.Act("start", a.Table!, 2);
            await Fails<InvalidOperationException>(() => f.Act("stand", a.Table!));
        });
        Case("waiting solo games persist without disconnect expiry", async f =>
        {
            var a = await f.Create("poker"); f.Now += 10000; f.Restart();
            Require((await f.Coordinator.PollAsync("alice")).Table!.Id == a.Table!.Id && f.Money.Read("alice").Held == 20);
        });
        Case("failed reservation keeps the original journal for one recovery", async f =>
        {
            f.Money.FailPlayer = "alice";
            var command = f.Intent("solo", game: "poker", amount: 20);
            await Fails<IOException>(() => f.Coordinator.ActAsync("alice", command));
            Require(f.Coordinator.Blocks("alice")); f.Money.FailPlayer = null; f.Restart();
            await f.Coordinator.RecoverAsync();
            Require((await f.Coordinator.ActAsync("alice", command)).Replayed && f.Money.Read("alice").Chips == 80);
        });
        Case("failed cashout recovers without duplicate chips", async f =>
        {
            var a = await f.Create("poker"); f.Money.FailPlayer = "alice";
            var command = f.Intent("leave", a.Table);
            await Fails<IOException>(() => f.Coordinator.ActAsync("alice", command));
            f.Money.FailPlayer = null; f.Restart(); await f.Coordinator.RecoverAsync();
            await f.Coordinator.ActAsync("alice", command); await f.Coordinator.RecoverAsync();
            Require(f.Money.Read("alice").Chips == 100 && f.Money.Read("alice").Held == 0);
        });
        Case("roulette zero resolves and pays exactly once", async f =>
        {
            f.Random = new Zero(); f.Restart();
            var a = await f.Create("roulette"); a = await f.Act("start", a.Table!);
            var command = f.Intent("bet", a.Table, amount: 1, selection: "number:0");
            a = await f.Coordinator.ActAsync("alice", command); await f.Coordinator.ActAsync("alice", command);
            f.Now += 21; a = await f.Coordinator.PollAsync("alice");
            Require(a.Table!.Roulette!.Number == 0 && a.Table.Roulette.Bets.Single().Stake == 1 && a.Table.Seats.Single().Stack == 55);
            f.Restart(); Require((await f.Coordinator.PollAsync("alice")).Table!.Seats.Single().Stack == 55);
        });
        test("saved table schema must be explicit", () =>
        {
            try { JsonSerializer.Deserialize<TableDatabase>("{}"); throw new Exception("Missing schema accepted"); } catch (JsonException) { }
        });
        Case("unsupported pending journal is preserved before wallet recovery", async f =>
        {
            await f.Create("poker");
            var next = f.Storage.Load(); next.Revision++; next.Tables[0].Solo = false;
            f.Storage.Prepare(new TableJournal(Guid.NewGuid().ToString("N"), next, []));
            var saved = JsonSerializer.Serialize(f.Storage.Pending());
            var account = f.Money.Read("alice");
            await Fails<InvalidDataException>(() => Task.Run(() => f.Restart()));
            Require(JsonSerializer.Serialize(f.Storage.Pending()) == saved && f.Money.Read("alice") == account);
        });
        Case("unsupported shared saves fail before changing reservations or journals", async f =>
        {
            await f.Create("poker");
            var saved = f.Storage.Load(); saved.Tables[0].Solo = false; f.Storage.Commit(saved);
            var before = JsonSerializer.Serialize(f.Storage.Load());
            var account = f.Money.Read("alice");
            await Fails<InvalidDataException>(() => Task.Run(() => f.Restart()));
            Require(JsonSerializer.Serialize(f.Storage.Load()) == before && f.Money.Read("alice") == account && f.Storage.Pending() == null);
        });
    }
    private static void Require(bool value) { if (!value) throw new Exception("Offline table invariant failed"); }
    private static async Task Fails<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "rippers-offline-" + Guid.NewGuid().ToString("N"));
        public long Now = 1000;
        public IRandomSource Random = new NoShuffle();
        public TestWallets Money;
        public TableStorage Storage;
        public TableCoordinator Coordinator = null!;
        public void Dispose() => Directory.Delete(directory, true);
        public Fixture() { Directory.CreateDirectory(directory); Money = new(directory); Money.Add("alice", "Alice"); Money.Add("bob", "Bob"); Storage = new(Path.Combine(directory, "tables")); Restart(); }
        public void Restart() => Coordinator = new(Money, Storage, new(), () => Now, Random);
        public TableIntent Intent(string operation, TableView? table = null, string game = "", int amount = 0, string selection = "") => new(Guid.NewGuid().ToString("N"), operation, table?.Id ?? "", table?.Version ?? 0, game, amount, selection);
        public Task<TableSnapshot> Create(string game) => Coordinator.ActAsync("alice", Intent("solo", game: game, amount: 20));
        public Task<TableSnapshot> Act(string operation, TableView table, int amount = 0, string selection = "") => Coordinator.ActAsync("alice", Intent(operation, table, amount: amount, selection: selection));
    }
    private sealed class NoShuffle : IRandomSource { public int Next(int maximum) => maximum - 1; }
    private sealed class Zero : IRandomSource { public int Next(int maximum) => 0; }
    private sealed class TestWallets(string directory) : ITableWallets
    {
        public string? FailPlayer;
        private readonly Dictionary<string, SemaphoreSlim> gates = new();
        private string PathFor(string id) => Path.Combine(directory, id + ".wallet.json");
        public void Add(string id, string name) => File.WriteAllText(PathFor(id), JsonSerializer.Serialize(new Saved(new(name, 100, 0, true, 1000), "")));
        private Saved Load(string id) => JsonSerializer.Deserialize<Saved>(File.ReadAllText(PathFor(id)))!;
        public TableAccount Read(string id) => Load(id).Account;
        public void Limit(string id, long limit)
        {
            var saved = Load(id);
            File.WriteAllText(PathFor(id), JsonSerializer.Serialize(saved with
            {
                Account = saved.Account with
                {
                    Limit = limit
                }
            }));
        }
        public async Task<IAsyncDisposable> LockAsync(IEnumerable<string> ids)
        {
            var held = new List<SemaphoreSlim>();
            foreach (var id in ids.Distinct().OrderBy(id => id))
            {
                if (!gates.TryGetValue(id, out var gate))
                    gates[id] = gate = new(1, 1);
                await gate.WaitAsync();
                held.Add(gate);
            }
            return new Lease(held);
        }
        public Task ApplyAsync(TableWalletChange change, string commit)
        {
            if (change.PlayerId == FailPlayer)
                throw new IOException("Injected wallet save failure");
            var before = Load(change.PlayerId);
            if (before.Commit == commit)
                return Task.CompletedTask;
            var account = before.Account with
            {
                Chips = before.Account.Chips + change.Chips,
                Held = before.Account.Held + change.Held,
                Liability = before.Account.Liability + change.Liability,
                Sequence = before.Account.Sequence + 1
            };
            File.WriteAllText(PathFor(change.PlayerId), JsonSerializer.Serialize(new Saved(account, commit)));
            return Task.CompletedTask;
        }
        private sealed record Saved(TableAccount Account, string Commit);
        private sealed class Lease(List<SemaphoreSlim> gates) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                foreach (var gate in gates.AsEnumerable().Reverse())
                    gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
