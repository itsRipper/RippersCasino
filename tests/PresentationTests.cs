using RippersCasino.Client;
internal static class PresentationTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(string name, Func<bool> run) => test(name, () => { if (!run()) throw new Exception(name); });
        Check("blackjack selection clamps 60 to 0 after 61-to-1 loss and recovers after deposit", () =>
        {
            var b = new BlackjackBetSelection(); b.Refresh(61, 10000);
            for (var i = 0; i < 29; i++) b.Step(1);
            if (b.Value != 60) return false;
            b.Refresh(1, 10000); if (b.Value != 0 || b.Maximum != 0) return false;
            b.Step(-1); b.Refresh(9, 10000); return b.Value == 2 && b.Maximum == 8;
        });
        Check("blackjack selection clamps after settlement, another seat loss and reconnect", () =>
        {
            var b = new BlackjackBetSelection(); b.Refresh(80, 10000); for (var i = 0; i < 40; i++) b.Step(1);
            b.Refresh(7, 10000); if (b.Value != 6) return false;
            b.Refresh(100, 4); return b.Value == 4 && b.Maximum == 4;
        });
        Check("blackjack every legal selection reaches minimum and stays within even bounds", () =>
        {
            for (var balance = 0; balance < 150; balance++)
            {
                var b = new BlackjackBetSelection(); b.Refresh(balance, 10000);
                for (var i = 0; i < 100; i++) b.Step(1);
                if (b.Value > balance || b.Value % 2 != 0) return false;
                for (var i = 0; i < 100; i++) b.Step(-1);
                if (b.Value != (balance < 2 ? 0 : 2)) return false;
            }
            return true;
        });
        Check("post-release / blackjack typed wagers, legal two-chip steps and legal bounds", () =>
        {
            var bet = new BlackjackBetSelection(); bet.Refresh(61, 1000);
            bet.Text = "20"; if (!bet.Valid || bet.Value != 20) return false;
            bet.Step(1); if (bet.Value != 22 || !bet.Valid) return false;
            bet.Refresh(61, 1000); if (bet.Text != "22") return false;
            bet.Step(1); if (bet.Value != 24 || !bet.Valid) return false;
            bet.Step(-1); if (bet.Value != 22 || !bet.Valid) return false;
            bet.Text = "-2"; if (bet.Valid) return false;
            bet.Text = "2.5"; if (bet.Valid) return false;
            bet.Text = "9999999999"; if (bet.Valid) return false;
            bet.Text = "62"; if (bet.Valid) return false;
            bet.Refresh(21, 1000); if (bet.Value != 20 || !bet.Valid) return false;
            bet.Text = "2"; bet.Step(-1); if (bet.Value != 2 || !bet.Valid) return false;
            bet.Refresh(1, 1000); if (bet.Value != 0 || bet.Valid) return false;
            bet.Refresh(80, 18); return bet.Value == 2 && bet.Maximum == 18 && bet.Valid;
        });
        Check("blackjack buttons normalize typed odd wagers in the selected direction", () =>
        {
            var bet = new BlackjackBetSelection(); bet.Refresh(61, 1000);
            bet.Text = "25"; if (bet.Valid) return false;
            bet.Step(-1); if (bet.Value != 24 || !bet.Valid) return false;
            bet.Text = "25"; bet.Step(1); if (bet.Value != 26 || !bet.Valid) return false;
            bet.Text = "9999999999"; bet.Step(-1);
            return bet.Value == 2 && bet.Valid;
        });
        Check("durable request creation refuses to overwrite a retained payment", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "casino-request-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "pending.json");
            try
            {
                DurableRequestFile.WriteNew(path, "original");
                try { DurableRequestFile.WriteNew(path, "replacement"); return false; }
                catch (IOException) { return File.ReadAllText(path) == "original"; }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Check("post-release / jackpot requires the exact highest-paying authoritative triple", () =>
        {
            int[] triples = [100,50,25,15,10,2];
            if (!JackpotPresentation.IsJackpot([0,0,0], triples, 5, 500)) return false;
            if (JackpotPresentation.IsJackpot([1,1,1], triples, 5, 250)) return false;
            if (JackpotPresentation.IsJackpot([0,0,1], triples, 5, 500)) return false;
            if (JackpotPresentation.IsJackpot([0,0,0], triples, 5, 499)) return false;
            if (JackpotPresentation.IsJackpot([0,0,0], triples, 0, 500)) return false;
            if (JackpotPresentation.IsJackpot([0,0,0], triples, long.MaxValue, long.MaxValue)) return false;
            if (JackpotPresentation.IsJackpot([0,0,0], [0,0,0,0,0,0], 5, 0)) return false;
            int[] custom = [50,100,25,15,10,2];
            return !JackpotPresentation.IsJackpot([0,0,0], custom, 5, 250) && JackpotPresentation.IsJackpot([1,1,1], custom, 5, 500);
        });
        Check("post-release / AUTO model respects an externally supplied jackpot readiness gate", () =>
        {
            var p = new SpinPresentation(); var auto = new AutoSpinSequence();
            auto.Start(true); if (!auto.TryBegin(true, true, true, 100, 5)) return false;
            p.Begin(100,5); p.Resolve([0,0,0],595,500); p.Tick(SpinPresentation.LastStop + 1);
            if (p.Active || p.VisibleBalance != 595) return false;
            auto.Complete(true);
            for (var frame = 0; frame < 120; frame++)
                if (auto.TryBegin(true, true, false, p.VisibleBalance, 5) || p.Tick(SpinPresentation.LastStop + 2) || p.VisibleBalance != 595) return false;
            return auto.Completed == 1 && auto.TryBegin(true,true,true,p.VisibleBalance,5) && !auto.TryBegin(true,true,true,p.VisibleBalance,5);
        });
        Check("roulette repeated quantized polls cannot jump its continuous clock", () =>
        {
            var r = new RoulettePresentation(); r.Observe("a", 100, 100, 500);
            double last = -1;
            for (var i = 0; i <= 480; i++)
            {
                var now = 500 + i / 120.0;
                if (i % 37 == 0) r.Observe("a", 100, 100 + i / 120, now);
                var p = r.Progress(now);
                if (p < last || p - last > .003 && last >= 0 || Math.Abs(p - i / 480.0) > .000001) return false;
                last = p;
            }
            return last == 1;
        });
        Check("roulette reconnect skips elapsed motion and all pockets align exactly", () =>
        {
            var r = new RoulettePresentation(); r.Observe("b", 100, 105, 800);
            if (r.Progress(800) != 1) return false;
            for (var pocket = 0; pocket < 37; pocket++)
                if (Math.Abs(RoulettePresentation.BallAngle(1, pocket) - (pocket * 360.0 / 37 - 90 + RoulettePresentation.WheelAngle(1))) > .000001) return false;
            r.Observe("c", 200, 200, 900); return r.Progress(900) == 0 && r.Progress(902) == .5;
        });
    }
}
