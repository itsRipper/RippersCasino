#nullable disable
using System;
namespace RippersCasino.Client
{
    // Presentation only. Server finish order and settlement never depend on frames.
    public sealed class RacePresentation
    {
        public const double FirstFinish = 3.74, FinishGap = .11, FinishTime = 4.4, CounterDuration = .4675;
        private readonly int[] _ranks = new int[6];
        private long _stakeBalance, _finalBalance, _payout;
        public bool Active { get; private set; }
        public bool Resolved { get; private set; }
        public bool Revealed { get; private set; }
        public long VisibleBalance { get; private set; }
        public long VisiblePayout => Revealed ? _payout : 0;
        public void Begin(long balance, int stake)
        {
            if (Active)
                throw new InvalidOperationException("A race is already being presented.");
            if (stake <= 0 || balance < stake)
                throw new ArgumentOutOfRangeException(nameof(stake));
            _stakeBalance = VisibleBalance = checked(balance - stake);
            _payout = 0;
            Active = true;
            Resolved = Revealed = false;
        }
        public void Resolve(int[] order, long balance, long payout)
        {
            if (!Active || Resolved || order == null || order.Length != 6 || balance < 0 || payout < 0 || balance != checked(_stakeBalance + payout))
                throw new InvalidOperationException("Invalid race receipt.");
            var seen = 0;
            for (var rank = 0; rank < 6; rank++)
            {
                var racer = order[rank];
                if (racer < 0 || racer >= 6 || (seen & (1 << racer)) != 0)
                    throw new InvalidOperationException("Invalid finish order.");
                seen |= 1 << racer;
            }
            for (var rank = 0; rank < 6; rank++)
                _ranks[order[rank]] = rank;
            _finalBalance = balance;
            _payout = payout;
            Resolved = true;
        }
        public double Position(int racer, double elapsed)
        {
            if (racer < 0 || racer >= 6)
                throw new ArgumentOutOfRangeException(nameof(racer));
            if (!Resolved)
                return 0;
            var u = Math.Max(0, Math.Min(1, elapsed / (FirstFinish + _ranks[racer] * FinishGap)));
            // Positive derivative throughout; differently phased surges cross lanes
            // without ever reversing or altering the authoritative finishing order.
            return u + .04 * Math.Sin(2 * Math.PI * u + racer * .9) * Math.Sin(Math.PI * u);
        }
        public bool Tick(double elapsed)
        {
            if (!Active || !Resolved || elapsed < FinishTime)
                return false;
            var first = !Revealed;
            Revealed = true;
            var u = Math.Max(0, Math.Min(1, (elapsed - FinishTime) / CounterDuration));
            VisibleBalance = checked(_stakeBalance + (long)Math.Round(_payout * u * u * (3 - 2 * u)));
            if (u >= 1 || _payout == 0)
            {
                VisibleBalance = _finalBalance;
                Active = false;
            }
            return first;
        }
        public void Reject(long balance)
        {
            Active = Resolved = Revealed = false;
            VisibleBalance = balance;
        }
    }
}
