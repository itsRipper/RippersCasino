using System;
using EFT.UI;
using UnityEngine;
using UnityEngine.Audio;

namespace RippersCasino.Client
{
    // Own only HideoutVolume while focused. Never touch snapshots, listener,
    // preferences, or individual environmental sources. Cooperate with native writes.
    internal sealed class HideoutAudioFocus : IDisposable
    {
        private AudioMixer _mixer;
        private float _baseline, _last, _weight;
        private readonly Action<string> _log;
        internal HideoutAudioFocus(Action<string> log)
        {
            _log = log;
        }
        internal void Tick(bool focused, float reduction, float delta)
        {
            if (_mixer == null)
            {
                if (!focused)
                    return;
                var gui = UnityEngine.Object.FindObjectOfType<GUISounds>();
                var candidate = gui != null ? gui.MasterMixer : null;
                if (candidate == null || !candidate.GetFloat("HideoutVolume", out _baseline))
                    return;
                _mixer = candidate;
                _last = _baseline;
                _weight = 0;
                _log?.Invoke("[RippersCasino] Hideout focus captured: " + _baseline + " dB; fade=.45s, reduction=" + reduction + " dB.");
            }
            if (!_mixer.GetFloat("HideoutVolume", out var current))
            {
                Restore();
                return;
            }
            if (Mathf.Abs(current - _last) > .01f)
                _baseline = current;
            _weight = Mathf.MoveTowards(_weight, focused ? 1 : 0, Mathf.Max(0, delta) / .45f);
            var eased = _weight * _weight * (3 - 2 * _weight);
            _last = Mathf.Max(-80, _baseline - reduction * eased);
            _mixer.SetFloat("HideoutVolume", _last);
            if (!focused && _weight <= 0)
                Restore();
        }
        internal void Restore()
        {
            if (_mixer != null)
            {
                // Scene exit/native settings may have already claimed this parameter.
                if (_mixer.GetFloat("HideoutVolume", out var current) && Mathf.Abs(current - _last) < .01f)
                    _mixer.SetFloat("HideoutVolume", _baseline);
                _log?.Invoke("[RippersCasino] Hideout focus released; previous gain restored unless native audio replaced it.");
            }
            _mixer = null;
            _weight = 0;
        }
        public void Dispose()
        {
            Restore();
        }
    }
}
