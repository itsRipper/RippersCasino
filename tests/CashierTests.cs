using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Core.Games;
using RippersCasino.Client;

internal static class CashierTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Action body) => test("economy / " + name, body);
        void Require(bool condition) { if (!condition) throw new Exception("Economy invariant failed."); }
        void Reject<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        var terms = new CashierTerms();
        var config = new CasinoConfig();
        var engine = new CasinoEngine(config, new NeverRandom());
        CasinoCommand Command(Wallet w, string operation, long amount, string currency = "RUB") => new(Guid.NewGuid().ToString("N"), w.NextSequence, operation, currency, amount, "rippers-slot", ExchangeVersion: 3);
        long WalletValue(Wallet w) => checked(w.Chips * 1000 + w.RemainderRub);
        long Value(Wallet w, Money m) => checked(WalletValue(w) + terms.RubPerCurrencyUnit.Sum(c => checked(m.Balance(c.Key) * c.Value)));
        foreach (var example in new[] { ("RUB", 100000L, 100L, 0), ("RUB", 1000000L, 1000L, 0), ("USD", 136L, 18L, 496), ("EUR", 158L, 24L, 964) })
            Check(example.Item1 + " deposit " + example.Item2 + " matches denomination example", () =>
            {
                var w = new Wallet(); var money = new Money(); money.Units[example.Item1] = example.Item2;
                var request = Command(w, "buy", example.Item2, example.Item1);
                var inventory = new DigitalChipInventory(w, money, config.MaxBalance);
                var result = engine.Apply(w, request, inventory);
                Require(w.Chips == example.Item3 && w.RemainderRub == example.Item4 && money.Balance(example.Item1) == 0 && result.CurrencyUnits == example.Item2);
                var snapshot = JsonSerializer.Serialize(w);
                engine.Apply(w, request, inventory);
                Require(JsonSerializer.Serialize(w) == snapshot);
            });
        foreach (var currency in terms.RubPerCurrencyUnit.Keys)
        {
            Check(currency + " zero destination balance supports withdrawals and exact reserve", () =>
            {
                var w = new Wallet { Chips = 50, RemainderRub = 29 }; var money = new Money(); money.Units[currency] = 0;
                var initial = Value(w, money);
                var preview = CashierAmount.Withdrawal("50", terms.RubPerCurrencyUnit[currency], 50, terms.MaxWithdrawalUnits(currency, 50, 29), 29);
                Require(preview.Valid && preview.Amount == 50);
                var request = Command(w, "redeem", 50, currency);
                var inventory = new DigitalChipInventory(w, money, config.MaxBalance);
                var result = engine.Apply(w, request, inventory);
                Require(w.Chips == 0 && w.RemainderRub == preview.RemainderRub && result.CurrencyUnits == preview.CurrencyUnits && Value(w, money) == initial);
                engine.Apply(w, request, inventory);
                Require(Value(w, money) == initial && money.Balance(currency) == preview.CurrencyUnits);
            });
            Check(currency + " small deposits accumulate, persist and match preview", () =>
            {
                var w = new Wallet(); var money = new Money(); var initial = Value(w, money);
                for (var i = 0; i < 1000; i++)
                {
                    var preview = CashierAmount.Deposit("1", terms.RubPerCurrencyUnit[currency], 1, w.RemainderRub);
                    var before = w.Chips;
                    engine.Apply(w, Command(w, "buy", 1, currency), new DigitalChipInventory(w, money, config.MaxBalance));
                    Require(preview.Valid && w.Chips - before == preview.Chips && w.RemainderRub == preview.RemainderRub && Value(w, money) == initial);
                    w = JsonSerializer.Deserialize<Wallet>(JsonSerializer.Serialize(w))!;
                    w.Validate();
                }
            });
        }
        foreach (var buy in terms.RubPerCurrencyUnit.Keys)
            foreach (var redeem in terms.RubPerCurrencyUnit.Keys)
                Check(buy + " to " + redeem + " repeated exchange cannot create or lose value", () =>
                {
                    var w = new Wallet(); var money = new Money(); var initial = Value(w, money);
                    for (var units = 1; units <= 200; units++)
                    {
                        engine.Apply(w, Command(w, "buy", units, buy), new DigitalChipInventory(w, money, config.MaxBalance));
                        if (terms.MaxWithdrawalUnits(redeem, w.Chips, w.RemainderRub) > 0)
                            engine.Apply(w, Command(w, "redeem", w.Chips, redeem), new DigitalChipInventory(w, money, config.MaxBalance));
                        Require(Value(w, money) == initial);
                    }
                });
        Check("cash reserve can be redeemed without a whole chip", () =>
        {
            var w = new Wallet { RemainderRub = 999 }; var money = new Money(); var before = money.Balance("RUB");
            engine.Apply(w, Command(w, "redeem", 0), new DigitalChipInventory(w, money, config.MaxBalance));
            Require(w.Chips == 0 && w.RemainderRub == 0 && money.Balance("RUB") == before + 999);
        });
        Check("MAX uses funds, reserves and actual representable capacity", () =>
        {
            foreach (var currency in terms.RubPerCurrencyUnit.Keys)
                foreach (var reserve in new[] { 0, 136, 999 })
                {
                    var max = terms.MaxDeposit(currency, long.MaxValue, CashierTerms.MaximumChips, reserve);
                    var quote = terms.Deposit(currency, max, reserve, CashierTerms.MaximumChips);
                    Require(quote.Chips <= CashierTerms.MaximumChips && quote.RemainderRub < 1000);
                    Require(terms.MaxDeposit(currency, 7, 10, reserve) == 7);
                }
            Require(terms.MaxDeposit("RUB", 10000, 0, 500) == 499);
        });
        Check("invalid amounts, protocols and overflow fail before inventory changes", () =>
        {
            var w = new Wallet { Chips = 10 }; var money = new Money(); var inventory = new DigitalChipInventory(w, money, config.MaxBalance);
            var initial = Value(w, money);
            Reject<ArgumentException>(() => engine.Apply(w, Command(w, "buy", -1), inventory));
            Reject<ArgumentException>(() => engine.Apply(w, Command(w, "redeem", 11), inventory));
            Reject<OverflowException>(() => engine.Apply(w, Command(w, "buy", long.MaxValue, "USD"), inventory));
            Reject<InvalidOperationException>(() => engine.Apply(w, Command(w, "buy", 1) with { EconomyVersion = 3 }, inventory));
            Reject<InvalidOperationException>(() => engine.Apply(w, Command(w, "buy", 1) with { ExchangeVersion = 2 }, inventory));
            Reject<InvalidOperationException>(() => new Wallet { Version = 3, Chips = 127000000000 }.Validate());
            Require(Value(w, money) == initial && w.NextSequence == 1);
        });
        Check("reservations remain whole chips and reduce deposit capacity", () =>
        {
            var w = new Wallet { Chips = 10, RemainderRub = 800, AdditionalData = new() { ["CasinoTableReceipt"] = JsonSerializer.SerializeToElement(new { Escrow = 20L, Liability = 30L }) } };
            Require(terms.MaxDeposit("RUB", 100000, CashierTerms.RemainingCapacity(100, w.Chips, w.TableEscrow, w.TableLiability), w.RemainderRub) == 40199);
            var quote = terms.Withdrawal("USD", w.Chips, w.Chips, w.RemainderRub);
            Require(quote.CurrencyUnits == 79 && quote.RemainderRub == 56);
        });
        Check("all game wagers retain whole-chip denomination and fixed odds", () =>
        {
            config.Validate();
            Require(config.Machine.Bets.SequenceEqual(new[] { 1, 5, 10, 25, 100, 500 }) && config.Tables.MaxBuyIn == 5000 && config.Tables.MaxWager == 1000);
            Require(new SlotRules(config.Machine).Statistics().Rtp == 26135d / 27000 && config.Racing.Racers.Select(r => r.ReturnMultiplier).SequenceEqual(new[] { 2, 3, 5, 7, 9, 11 }));
            var w = new Wallet { Chips = 100, RemainderRub = 496 };
            new CasinoEngine(config, new Tickets([0, 1, 3])).Apply(w, new(Guid.NewGuid().ToString("N"), 1, "spin", "RUB", 1, "rippers-slot"), new DigitalChipInventory(w, new Money(), config.MaxBalance));
            Require(w.Chips == 99 && w.RemainderRub == 496);
        });
        Check("Blackjack pays exact 3:2 profit on every supported even stake", () =>
        {
            foreach (var bet in new[] { 2, 4, 10, 100, 1000 })
            {
                var hand = new BlackjackHand { Position = 0, Bet = bet, Bankroll = bet * 2, Cards = [0, 9] };
                var round = new BlackjackRound { Hands = [hand], Dealer = [8, 21], Deadline = 100 };
                Blackjack.Act(round, 0, "stand", 1, 45);
                Require(round.Phase == "complete" && hand.Profit == bet * 3L / 2);
            }
            Reject<ArgumentException>(() => Blackjack.Deal([new() { Bet = 1 }], new NeverRandom(), 6, 0, 45));
        });
        Check("modified rates and excess wallet value are rejected", () =>
        {
            var c = new CasinoConfig(); c.Cashier.RubPerCurrencyUnit["USD"] = 135; Reject<InvalidOperationException>(c.Validate);
            Reject<InvalidOperationException>(() => new Wallet { RemainderRub = 1000 }.Validate());
            Reject<InvalidOperationException>(() => new Wallet { Chips = long.MaxValue }.Validate());
        });
    }
}
