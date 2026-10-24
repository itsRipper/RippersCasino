namespace RippersCasino.Client
{
    internal sealed class AutoSpinSequence
    {
        internal bool Active { get; private set; }
        internal bool InFlight { get; private set; }
        internal int Completed { get; private set; }
        internal bool Start(bool eligible)
        {
            if (!eligible || Active || InFlight) return false;
            Active = true;
            Completed = 0;
            return true;
        }
        internal void Stop() => Active = false;
        internal bool TryBegin(bool visible, bool connected, bool ready, long balance, int stake)
        {
            if (!visible || !connected) Stop();
            if (!Active || InFlight || !ready) return false;
            if (stake < 1 || balance < stake) { Stop(); return false; }
            InFlight = true;
            return true;
        }
        internal void Complete(bool success)
        {
            if (!InFlight) return;
            InFlight = false;
            if (success) Completed++;
            else Stop();
        }
    }
}
