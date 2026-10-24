using RippersCasino.Client;
using RippersCasino.Core;
internal static class PublicReleaseTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Func<bool> condition) => test(name, () => { if (!condition()) throw new Exception(name); });
        Check("polish / Russian Roulette supports typed full-bankroll wagers and consistent steps", () =>
        {
            var bet = new RoundWagerSelection(); bet.Refresh(100); bet.Text = "100";
            if (!bet.Valid || bet.Value != 100) return false;
            bet.Step(1); if (bet.Value != 100) return false;
            bet.Step(-1); if (bet.Value != 99) return false;
            bet.Text = "101"; if (bet.Valid) return false;
            bet.Refresh(5); if (bet.Value != 5 || !bet.Valid) return false;
            bet.Text = "1.5"; if (bet.Valid) return false;
            bet.Step(-1); if (bet.Value != 1) return false;
            bet.Refresh(0); if (bet.Valid || bet.Value != 0) return false;
            bet.Refresh(int.MaxValue); bet.Text = int.MaxValue.ToString(); bet.Step(1);
            return bet.Valid && bet.Value == int.MaxValue;
        });
        Check("polish / triple lemons pay 2x with a positive house edge and losing combinations", () =>
        {
            var config = new CasinoConfig(); var rules = new SlotRules(config.Machine); config.Validate();
            var stats = rules.Statistics();
            return rules.Multiplier(new[] { "LEMON", "LEMON", "LEMON" }) == 2 && rules.Multiplier(new[] { "7", "DIAMOND", "BAR" }) == 0 && Math.Abs(stats.Rtp - 26135d / 27000) < 1e-12 && Math.Abs(stats.HitFrequency - 8100d / 27000) < 1e-12 && stats.Rtp < 1;
        });
        Check("AUTO start and duplicate clicks cannot create concurrent requests", () =>
        {
            var a = new AutoSpinSequence();
            return a.Start(true) && !a.Start(true) && a.TryBegin(true, true, true, 10, 1) && !a.TryBegin(true, true, true, 10, 1) && !a.Start(true);
        });
        Check("STOP AUTO finishes its current spin without queuing another", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); a.TryBegin(true, true, true, 10, 1); a.Stop();
            if (!a.InFlight || a.Active || a.TryBegin(true, true, true, 10, 1)) return false;
            a.Complete(true); return !a.InFlight && !a.Active && a.Completed == 1;
        });
        Check("AUTO waits for presentation and wallet readiness", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true);
            if (a.TryBegin(true, true, false, 10, 1) || !a.Active) return false;
            a.TryBegin(true, true, true, 10, 1); a.Complete(true);
            return a.TryBegin(true, true, true, 9, 1) && a.Completed == 1;
        });
        Check("AUTO does not mistake an unresolved all-in stake for a final loss", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); a.TryBegin(true, true, true, 1, 1);
            if (a.TryBegin(true, true, false, 0, 1) || !a.Active) return false;
            a.Complete(true); return a.TryBegin(true, true, true, 100, 1);
        });
        Check("AUTO stops on insufficient funds without a request", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); return !a.TryBegin(true, true, true, 0, 1) && !a.Active && !a.InFlight;
        });
        Check("AUTO stops when leaving page, closing or losing focus", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); return !a.TryBegin(false, true, true, 10, 1) && !a.Active;
        });
        Check("AUTO connection loss stops future spins and preserves the current request", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); a.TryBegin(true, true, true, 10, 1);
            if (a.TryBegin(true, false, true, 10, 1) || a.Active || !a.InFlight) return false;
            a.Complete(false); return !a.InFlight && a.Completed == 0;
        });
        Check("AUTO cannot resume itself after a rejected receipt", () =>
        {
            var a = new AutoSpinSequence(); a.Start(true); a.TryBegin(true, true, true, 10, 1); a.Complete(false);
            return !a.Active && !a.TryBegin(true, true, true, 10, 1) && a.Start(true);
        });
        Check("sequential AUTO losses use normal durable receipts and stop at zero", () =>
        {
            var a = new AutoSpinSequence(); var wallet = new Wallet { Chips = 10 }; var money = new Money(0);
            var engine = new CasinoEngine(new CasinoConfig(), new Tickets([0, 1, 3])); a.Start(true);
            for (var i = 0; i < 10; i++)
            {
                if (!a.TryBegin(true, true, true, wallet.Chips, 1)) return false;
                var command = new CasinoCommand(Guid.NewGuid().ToString("N"), wallet.NextSequence, "spin", "RUB", 1, "rippers-slot");
                var receipt = engine.Apply(wallet, command, new DigitalChipInventory(wallet, money, 10000));
                var after = wallet.Chips;
                engine.Apply(wallet, command, new DigitalChipInventory(wallet, money, 10000));
                if (wallet.Chips != after || receipt.Payout != 0 || wallet.Chips != 9 - i) return false;
                var p = new SpinPresentation(); p.Begin(after + 1, 1); p.Resolve([0, 1, 2], after, 0); p.Tick(SpinPresentation.LastStop);
                if (p.Active) return false;
                a.Complete(true);
            }
            return a.Completed == 10 && !a.TryBegin(true, true, true, wallet.Chips, 1) && !a.Active;
        });
        Check("AUTO wins use the same stake and payout exactly once", () =>
        {
            var wallet = new Wallet { Chips = 10 }; var engine = new CasinoEngine(new CasinoConfig(), new Tickets([0, 0, 0]));
            var command = new CasinoCommand(Guid.NewGuid().ToString("N"), wallet.NextSequence, "spin", "RUB", 1, "rippers-slot");
            var inventory = new DigitalChipInventory(wallet, new Money(0), 10000);
            var result = engine.Apply(wallet, command, inventory); engine.Apply(wallet, command, inventory);
            return result.Payout == 100 && wallet.Chips == 109;
        });
        Check("revolver cylinder rotates continuously, locks before firing and retains its position", () =>
        {
            var previous = RevolverMotion.Cylinder(0, 1, true);
            double travel = 0;
            for (var i = 1; i <= 550; i++)
            {
                var angle = RevolverMotion.Cylinder(i / 1000.0, 1, true);
                var delta = (angle - previous + 540) % 360 - 180;
                if (delta < -.001 || delta > 4) return false;
                travel += delta;
                previous = angle;
            }
            return Math.Abs(travel - 420) < .01 && previous == 60 && RevolverMotion.Cylinder(.64, 1, true) == 60 && RevolverMotion.Cylinder(0, 2, true) == 60;
        });
        Check("revolver hammer strikes with the trigger and returns without dry-fire recoil", () =>
            RevolverMotion.Hammer(0, true) == 24 && RevolverMotion.Hammer(.5, true) == 0 && RevolverMotion.Hammer(RevolverMotion.TriggerAt, true) == 24 && RevolverMotion.Trigger(RevolverMotion.TriggerAt, true) == 12 && RevolverMotion.Trigger(1, true) == 0 && RevolverMotion.Hammer(.5, false) == 24);
        Check("live revolver shot fires at animation trigger exactly once", () =>
        {
            var r = new RevolverTrigger(); r.Seed("a", 0); r.Schedule("a", 1, "eliminated", 10);
            if (r.TryFire(10.64, out _) || !r.TryFire(10.65, out var sound) || sound != RevolverSound.Gunshot) return false;
            r.Schedule("a", 1, "eliminated", 12); return !r.TryFire(20, out _);
        });
        Check("empty revolver chamber produces dry fire instead of a gunshot", () =>
        {
            var r = new RevolverTrigger(); r.Seed("a", 0); r.Schedule("a", 1, "safe", 0);
            return r.TryFire(.65, out var sound) && sound == RevolverSound.DryFire;
        });
        Check("revolver forfeit has no gunshot and reconnect does not replay old events", () =>
        {
            var r = new RevolverTrigger(); r.Seed("a", 3); r.Schedule("a", 3, "eliminated", 1);
            if (r.TryFire(20, out _)) return false;
            r.Schedule("a", 4, "forfeit", 1); return r.TryFire(2, out var sound) && sound == RevolverSound.None;
        });
        Check("compact roulette artwork has no fake white result while real zero remains valid", () =>
            RoulettePresentation.Caption(-1, 1, true) == "" && RoulettePresentation.Caption(0, 1, true) == "" &&
            RoulettePresentation.Caption(0, 1, false) == "0" && RoulettePresentation.Caption(0, .5, false).Contains("SINGLE ZERO") &&
            RippersCasino.Core.Games.Roulette.Wheel.Count(n => n == 0) == 1);
    }
}
