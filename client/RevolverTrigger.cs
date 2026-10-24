namespace RippersCasino.Client
{
    internal enum RevolverSound { None, DryFire, Gunshot }
    internal sealed class RevolverTrigger
    {
        private string _round = string.Empty;
        private int _sequence;
        private double _due;
        private RevolverSound _sound;
        internal bool Pending { get; private set; }
        internal void Seed(string round, int sequence)
        {
            _round = round;
            _sequence = sequence;
            Pending = false;
        }
        internal void Schedule(string round, int sequence, string result, double now)
        {
            if (_round != round) { Seed(round, sequence); return; }
            if (sequence <= _sequence) return;
            _sequence = sequence;
            _sound = result == "eliminated" ? RevolverSound.Gunshot : result == "safe" ? RevolverSound.DryFire : RevolverSound.None;
            _due = now + RevolverMotion.TriggerAt;
            Pending = true;
        }
        internal bool TryFire(double now, out RevolverSound sound)
        {
            sound = RevolverSound.None;
            if (!Pending || now < _due) return false;
            Pending = false;
            sound = _sound;
            return true;
        }
    }
}
