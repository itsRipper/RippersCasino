namespace RippersCasino.Client
{
    internal sealed class CasinoReadSession
    {
        internal bool Active { get; private set; }
        internal long Generation { get; private set; }
        internal bool Begin()
        {
            if (Active) return false;
            Generation++;
            Active = true;
            return true;
        }
        internal void End()
        {
            if (!Active) return;
            Active = false;
            Generation++;
        }
        internal bool CanPoll(bool inHideout) => Active && inHideout;
        internal bool Accept(long generation) => Active && generation == Generation;
    }
}
