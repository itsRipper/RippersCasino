#nullable disable
using System;

namespace RippersCasino.Client
{
    // Unwrapped symbol phases survive every spin. Receipt time only changes the
    // trajectory, never the sampled position; modulo belongs exclusively to drawing.
    public sealed class SpinPresentation
    {
        public const double FirstStop = 1.875, SecondStop = 2.325, LastStop = 2.775, CounterDuration = .6375;
        private readonly double[] _stops = { FirstStop, SecondStop, LastStop };
        private readonly double[] _phase = new double[3], _start = new double[3], _handoff = new double[3], _travel = new double[3];
        private double _velocity;
        private int _count = 6;
        private long _finalBalance, _payout, _stakeBalance;
        public bool Active { get; private set; }
        public bool Resolved { get; private set; }
        public bool Revealed { get; private set; }
        public long VisibleBalance { get; private set; }
        public long VisiblePayout => Revealed ? _payout : 0;
        public void SetRest(int[] symbols, int count = 6)
        {
            if (Active)
                throw new InvalidOperationException("Cannot reseed moving reels.");
            if (symbols == null || symbols.Length != 3 || count < 1 || Array.Exists(symbols, x => x < 0 || x >= count))
                throw new ArgumentException("Invalid strip symbols.");
            _count = count;
            for (var i = 0; i < 3; i++)
                _phase[i] = symbols[i];
        }
        public void Begin(long before, int stake)
        {
            if (Active)
                throw new InvalidOperationException("A spin is already being presented.");
            if (stake <= 0 || before < stake)
                throw new ArgumentOutOfRangeException(nameof(stake));
            Array.Copy(_phase, _start, 3);
            VisibleBalance = _stakeBalance = checked(before - stake);
            Active = true;
            Resolved = Revealed = false;
            _payout = 0;
        }
        private static double WaitingDistance(double t) => 15 * (t - .3 * (1 - Math.Exp(-t / .3)));
        public void Resolve(int[] targets, long finalBalance, long payout, double waitingElapsed = 0, int count = 6)
        {
            if (!Active || Resolved || targets == null || targets.Length != 3 || count < 1 || Array.Exists(targets, x => x < 0 || x >= count) || finalBalance < 0 || payout < 0 || double.IsNaN(waitingElapsed) || double.IsInfinity(waitingElapsed))
                throw new InvalidOperationException("Invalid presentation result.");
            _count = count;
            var t = Math.Max(0, waitingElapsed);
            _velocity = 15 * (1 - Math.Exp(-t / .3));
            for (var i = 0; i < 3; i++)
            {
                _handoff[i] = _start[i] + WaitingDistance(t);
                // At least four rotations and sufficient distance for a monotone
                // quintic with this entry velocity and a zero-velocity landing.
                var minimum = Math.Max((4 + i) * count, _velocity * _stops[i] * .7);
                var target = targets[i] + Math.Ceiling((_handoff[i] + minimum - targets[i]) / count) * count;
                _travel[i] = target - _handoff[i];
                _phase[i] = target;
            }
            _finalBalance = finalBalance;
            _payout = payout;
            Resolved = true;
        }
        public bool Tick(double elapsed)
        {
            if (!Active || !Resolved || elapsed < LastStop)
                return false;
            var first = !Revealed;
            Revealed = true;
            var progress = Math.Max(0, Math.Min(1, (elapsed - LastStop) / CounterDuration));
            var eased = progress * progress * (3 - 2 * progress);
            VisibleBalance = checked(_stakeBalance + (long)Math.Round(_payout * eased));
            if (progress >= 1 || _payout == 0)
            {
                VisibleBalance = _finalBalance;
                Active = false;
            }
            return first;
        }
        public bool Stopped(int reel, double elapsed) => Resolved && elapsed >= _stops[reel];
        public double Position(int reel, double elapsed, int count)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (reel < 0 || reel >= 3)
                throw new ArgumentOutOfRangeException(nameof(reel));
            if (count != _count)
                throw new InvalidOperationException("Strip length changed during presentation.");
            if (!Active)
                return _phase[reel];
            var t = Math.Max(0, elapsed);
            if (!Resolved)
                return _start[reel] + WaitingDistance(t);
            if (t >= _stops[reel])
                return _phase[reel];
            var u = t / _stops[reel];
            var u2 = u * u;
            var u3 = u2 * u;
            var u4 = u3 * u;
            var u5 = u4 * u;
            return _handoff[reel] + _travel[reel] * (10 * u3 - 15 * u4 + 6 * u5) + _velocity * _stops[reel] * (u - 6 * u3 + 8 * u4 - 3 * u5);
        }
        public void Reject(long authoritativeBalance)
        {
            Active = false;
            Resolved = false;
            Revealed = false;
            VisibleBalance = authoritativeBalance;
            Array.Copy(_start, _phase, 3);
        }
    }
    internal static class JackpotPresentation
    {
        internal const double Duration = 2.8;
        internal static bool IsJackpot(int[] reels, int[] triples, long wager, long payout)
        {
            if (reels == null || reels.Length != 3 || triples == null || wager <= 0 || payout <= 0) return false;
            var symbol = reels[0];
            if (symbol < 0 || symbol >= triples.Length || reels[1] != symbol || reels[2] != symbol) return false;
            var highest = 0;
            foreach (var multiplier in triples) highest = Math.Max(highest, multiplier);
            return highest > 0 && triples[symbol] == highest && payout / wager == highest && payout % wager == 0;
        }
    }

}
