namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private readonly AutoSpinSequence _autoSpin = new AutoSpinSequence();
        private void TickAutoSpin()
        {
            var visible = _open && _page == 1 && UnityEngine.Application.isFocused;
            var connected = CanUse && !_restart && _state?.enabled == true && _state.available && _state.error == null;
            if (!visible || !connected) _autoSpin.Stop();
            if (_autoSpin.InFlight && !GameActive && !_busy && _pending == null) _autoSpin.Complete(true);
            if (_autoSpin.TryBegin(visible, connected, Available && (_laptopSession?.Ready ?? true), _shownBalance, _state?.bets[_bet] ?? 0))
            {
                Begin("spin", _state.bets[_bet]);
                if (_pending == null && !_presentation.Active) _autoSpin.Complete(false);
            }
        }
        private void StopAutoOnError()
        {
            _autoSpin.Stop();
            _autoSpin.Complete(false);
        }
    }
}
