using System;
using System.Collections.Generic;
using EFT.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RippersCasino.Client
{
    internal enum CasinoCue
    {
        Button, Bet, Lever, ReelStop, Win, Jackpot, Payout, Exchange, Open, CardDeal, ChipPlace, WheelSpin, BallDrop, Cylinder, SafeClick, Gunshot, Hover, Error, CardFlip, RaceStart, RaceStep, RaceFinish, SmallWin
    }
    // Generated PCM is invalidated by EFT.AudioUtils.ResetAudioBuffer. Rebuild
    // after initialization/device changes, never once at BepInEx Awake alone.
    internal sealed class CasinoAudio : IDisposable
    {
        private readonly GameObject _root;
        private readonly Func<float> _volume, _effectsGain, _reelsGain;
        private readonly Func<bool> _diagnostics;
        private readonly Action<string> _log;
        private readonly Dictionary<CasinoCue, AudioClip> _cues = new Dictionary<CasinoCue, AudioClip>();
        private readonly AudioSource _effects, _motor, _ambient;
        private AudioClip _rolling, _hum;
        private readonly NativeRevolverShot _nativeShot;
        private GUISounds _guiSounds;
        private bool _dirty = true, _disposed, _ready, _probePending;
        private float _readyAt, _gain, _probeAt;
        private readonly float[] _probe = new float[1024];
        private static readonly float[] WinNotes = { 523.251f, 659.255f, 783.991f, 1046.502f, 1318.51f };
        private static readonly float[] JackpotNotes = { 523.251f, 659.255f, 783.991f, 1046.502f, 783.991f, 1046.502f, 1318.51f, 1567.982f, 2093.004f };
        internal CasinoAudio(GameObject owner, Func<float> volume, Func<float> effects, Func<float> reels, Func<bool> diagnostics, Action<string> log)
        {
            _nativeShot = new NativeRevolverShot(log);
            _volume = volume;
            _effectsGain = effects;
            _reelsGain = reels;
            _diagnostics = diagnostics;
            _log = log;
            _root = new GameObject("RippersCasino sound stage");
            _root.transform.SetParent(owner.transform, false);
            _effects = Source("Casino effects", false);
            _motor = Source("Casino reel motor", true);
            _ambient = Source("Casino electrical bed", true);
            AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;
        }
        private AudioSource Source(string name, bool loop)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root.transform, false);
            var source = node.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.ignoreListenerPause = false;
            source.ignoreListenerVolume = false;
            source.priority = 40;
            source.dopplerLevel = 0;
            return source;
        }
        private void ConfigurationChanged(bool deviceChanged)
        {
            if (_disposed)
                return;
            _dirty = true;
            _ready = false;
            _readyAt = Time.unscaledTime + .25f;
            if (_diagnostics()) _log?.Invoke("[RippersCasino] Audio configuration reset; invalidated PCM bank will rebuild after native audio stabilizes. device=" + deviceChanged);
        }
        private bool EnsureReady()
        {
            if (_disposed || Time.unscaledTime < _readyAt)
                return false;
            if (_guiSounds == null)
                _guiSounds = Object.FindObjectOfType<GUISounds>();
            var group = _guiSounds != null ? _guiSounds.GetCommonSoundsMixerGroup() : null;
            if (group == null)
                return false;
            if (_rolling != null && _rolling.samples == 0)
                _dirty = true;
            if (_dirty)
            {
                ClearBank();
                _cues[CasinoCue.Button] = Clip("illuminated switch", .1f, Button);
                _cues[CasinoCue.Bet] = Clip("stake selector", .15f, t => Button(t) * .7f + Bell(t - .025f, 1320) * .18f);
                _cues[CasinoCue.Lever] = Clip("lever ratchet and spin rise", .48f, Lever);
                _cues[CasinoCue.ReelStop] = Clip("reel pawl and bell", .24f, Stop);
                _cues[CasinoCue.Win] = Clip("payout fanfare", 1.65f, t => Fanfare(t, false));
                _cues[CasinoCue.Jackpot] = Clip("jackpot fanfare", 3.3f, t => Fanfare(t, true));
                _cues[CasinoCue.Payout] = Clip("ceramic chips cascade", .85f, Payout);
                _cues[CasinoCue.Exchange] = Clip("cashier confirmation", .65f, t => Bell(t, 783.991f) * .5f + Bell(t - .12f, 1046.502f) * .4f + Payout(t - .2f) * .3f);
                _cues[CasinoCue.Open] = Clip("welcome bell", .42f, t => Bell(t, 659.255f) * .65f + Bell(t - .13f, 987.767f) * .45f);
                _cues[CasinoCue.CardDeal] = Clip("card felt slide", .18f, Paper);
                _cues[CasinoCue.ChipPlace] = Clip("ceramic chip placement", .22f, Ceramic);
                _cues[CasinoCue.WheelSpin] = Clip("roulette wheel and rolling ball", 4, t => (Noise(t) * .06f + Sine(190 + 30 * Mathf.Sin(t * 3), t) * .035f + Stop((t * (12 - t)) % 1) * .14f) * Mathf.Clamp01((4 - t) / 1.2f));
                _cues[CasinoCue.BallDrop] = Clip("roulette pocket drop", .45f, t => Stop(t) * .7f + Payout(t) * .15f);
                _cues[CasinoCue.Cylinder] = Clip("virtual cylinder ratchet", .65f, t => Lever(t) * .4f + Bell(t, 160) * .14f);
                _cues[CasinoCue.SafeClick] = Clip("hammer dry fire", .14f, t => Noise(t) * Mathf.Exp(-t * 150) * .7f + Sine(1800, t) * Mathf.Exp(-t * 130) * .2f);
                _cues[CasinoCue.Hover] = Clip("switch contact", .045f, t => Noise(t) * Mathf.Exp(-t * 180) * .16f);
                _cues[CasinoCue.Error] = Clip("relay refusal", .22f, t => Button(t) * .6f + (t >= .09f ? Button(t - .09f) : 0) * .2f);
                _cues[CasinoCue.CardFlip] = Clip("paper turn", .16f, Paper);
                _cues[CasinoCue.RaceStart] = Clip("arcade start bell", .5f, t => Bell(t, 660) * .3f + (t >= .18f ? Stop(t - .18f) : 0) * .2f);
                _cues[CasinoCue.RaceStep] = Clip("concrete foot contact", .095f, t => Noise(t) * Mathf.Exp(-t * 72) * .35f + Sine(110, t) * Mathf.Exp(-t * 55) * .3f);
                _cues[CasinoCue.RaceFinish] = Clip("finish line bell", .6f, t => Bell(t, 880) * .4f + Bell(t - .12f, 1320) * .25f);
                _cues[CasinoCue.SmallWin] = Clip("small payout bell", .65f, t => Bell(t, 1046) * .35f + Ceramic(Mathf.Max(0, t - .08f)) * .2f);
                _rolling = Clip("seamless reel loop", 1, Rolling, false);
                _hum = Clip("electrical bed", 2, Hum, false);
                _motor.clip = _rolling;
                _ambient.clip = _hum;
                _dirty = false;
                _log?.Invoke("[RippersCasino] PCM bank built after native initialization: " + _cues.Count + " cues, motor=" + _rolling.samples + " samples; reusable sources=3.");
            }
            if (_effects.outputAudioMixerGroup != group)
                _effects.outputAudioMixerGroup = group;
            if (_motor.outputAudioMixerGroup != group)
                _motor.outputAudioMixerGroup = group;
            if (_ambient.outputAudioMixerGroup != group)
                _ambient.outputAudioMixerGroup = group;
            _ready = true;
            return true;
        }
        internal void Tick(bool hideoutVisible, bool open, bool spinning)
        {
            var audible = hideoutVisible && open;
            if (!audible) return;
            _gain = Mathf.MoveTowards(_gain, 1, Time.unscaledDeltaTime / .18f);
            EnsureReady();
            _nativeShot.Prepare();
            var volume = Mathf.Clamp01(_volume()) * _gain;
            _effects.volume = volume * Mathf.Clamp01(_effectsGain());
            _motor.volume = volume * .55f * Mathf.Clamp01(_reelsGain());
            _ambient.volume = volume * .035f;
            if (_ready && !_ambient.isPlaying)
                _ambient.Play();
            if (_ready && spinning && !_motor.isPlaying)
                _motor.Play();
            if (!spinning && _motor.isPlaying)
                _motor.Stop();
            if (_probePending && Time.unscaledTime >= _probeAt)
            {
                _probePending = false;
                Probe();
            }
        }
        internal void Play(CasinoCue cue)
        {
            if (!EnsureReady()) return;
            _effects.volume = Mathf.Clamp01(_volume()) * Mathf.Clamp01(_effectsGain()) * Mathf.Max(.01f, _gain);
            if (cue == CasinoCue.Gunshot)
            {
                if (!_nativeShot.TryPlay(_effects)) return;
            }
            else
            {
                if (!_cues.TryGetValue(cue, out var clip) || clip.samples == 0) return;
                _effects.PlayOneShot(clip, cue == CasinoCue.Hover ? .07f : cue == CasinoCue.RaceStep ? .12f : cue == CasinoCue.Button || cue == CasinoCue.CardDeal || cue == CasinoCue.CardFlip ? .4f : 1);
            }
            if (_diagnostics() && cue == CasinoCue.Gunshot)
                _log?.Invoke("[RippersCasino] Revolver shot source: native RSh-12");
            if (_diagnostics() && (cue == CasinoCue.Lever || cue == CasinoCue.Open || cue == CasinoCue.Gunshot))
            {
                _probePending = true;
                _probeAt = Time.unscaledTime + (cue == CasinoCue.Gunshot ? .08f : .35f);
            }
        }
        internal void StopSpin()
        {
            _motor.Stop();
        }
        internal void Suspend()
        {
            _effects.Stop();
            _nativeShot.Release();
            _motor.Stop();
            _ambient.Stop();
            _gain = 0;
            _probePending = false;
        }
        private void Probe()
        {
            _effects.GetOutputData(_probe, 0);
            var peak = 0f;
            foreach (var v in _probe)
                peak = Mathf.Max(peak, Mathf.Abs(v));
            _motor.GetOutputData(_probe, 0);
            var motorPeak = 0f;
            foreach (var v in _probe)
                motorPeak = Mathf.Max(motorPeak, Mathf.Abs(v));
            var hideoutDb = float.NaN;
            if (_guiSounds != null && _guiSounds.MasterMixer != null)
                _guiSounds.MasterMixer.GetFloat("HideoutVolume", out hideoutDb);
            _log?.Invoke("[RippersCasino] Audio output: effectsPlaying=" + _effects.isPlaying + " peak=" + peak + " motorPeak=" + motorPeak + " hideoutDb=" + hideoutDb + " motorPlaying=" + _motor.isPlaying + " motorSamples=" + _rolling?.samples + " listenerVolume=" + AudioListener.volume + " listenerPaused=" + AudioListener.pause + " group=" + _effects.outputAudioMixerGroup?.name);
        }
        private static AudioClip Clip(string name, float duration, Func<float, float> synth, bool fade = true)
        {
            const int rate = 44100;
            var data = new float[(int)(duration * rate)];
            var peak = .001f;
            for (var i = 0; i < data.Length; i++)
            {
                var t = i / (float)rate;
                var sample = synth(t);
                if (fade)
                    sample *= Mathf.Min(1, t / .003f) * Mathf.Min(1, (duration - t) / .018f);
                data[i] = sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            var gain = .78f / peak;
            for (var i = 0; i < data.Length; i++)
                data[i] *= gain;
            var clip = AudioClip.Create("RippersCasino original " + name, data.Length, 1, rate, false);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!clip.SetData(data, 0))
            {
                Object.Destroy(clip);
                throw new InvalidOperationException("Casino PCM upload failed: " + name);
            }
            return clip;
        }
        private static float Sine(float hz, float t) => Mathf.Sin(2 * Mathf.PI * hz * t);
        private static float Noise(float t)
        {
            var n = (uint)(t * 44100) + 0x9e3779b9u;
            n ^= n << 13;
            n ^= n >> 17;
            n ^= n << 5;
            return (n & 65535) / 32767.5f - 1f;
        }
        private static float Bell(float t, float hz)
        {
            if (t < 0 || t > 1.4f)
                return 0;
            return (Sine(hz, t) * Mathf.Exp(-t * 4.5f) + Sine(hz * 2.76f, t) * Mathf.Exp(-t * 14) * .35f + Sine(hz * 5.4f, t) * Mathf.Exp(-t * 24) * .12f) * Mathf.Min(1, t / .002f);
        }
        private static float Paper(float t)
        {
            var envelope = Mathf.Sin(Mathf.Clamp01(t / .18f) * Mathf.PI);
            return (Noise(t) * .10f + Noise(t * .23f) * .07f) * envelope * Mathf.Exp(-t * 7) + Noise(t) * Mathf.Exp(-t * 100) * .08f;
        }
        private static float Ceramic(float t) => (Sine(2430, t) * Mathf.Exp(-t * 35) + Sine(3810, t) * Mathf.Exp(-t * 52) * .4f + Noise(t) * Mathf.Exp(-t * 170) * .16f) * Mathf.Min(1, t / .001f);
        private static float Button(float t) => Noise(t) * Mathf.Exp(-t * 65) * .7f + Sine(280, t) * Mathf.Exp(-t * 48) * .55f;
        private static float Lever(float t)
        {
            var value = Sine(90 + 80 * t, t) * Mathf.Exp(-t * 8) * .55f;
            for (var i = 0; i < 6; i++)
            {
                var k = t - i * .043f;
                if (k >= 0 && k < .04f)
                    value += Noise(k) * Mathf.Exp(-k * 95) * (.25f + i * .05f);
            }
            return value + Bell(t - .25f, 880) * .24f;
        }
        private static float Stop(float t) => Noise(t) * Mathf.Exp(-t * 75) * .65f + Sine(95, t) * Mathf.Exp(-t * 30) * .8f + Bell(t - .012f, 1174.66f) * .25f;
        private static float Rolling(float t)
        {
            // Integer fundamental and pulse counts give a seamless one-second loop.
            var pulse = Mathf.Pow(Mathf.Max(0, Sine(28, t)), 12);
            return Sine(84, t) * .2f + Sine(168, t) * .07f + pulse * (Sine(1344, t) * .2f + Sine(700, t) * .11f);
        }
        private static float Hum(float t) => Sine(60, t) * .3f + Sine(120, t) * .1f + Sine(240, t) * .025f + Sine(660, t) * (.025f + .02f * Sine(.5f, t));
        private static float Payout(float t)
        {
            var value = 0f;
            for (var i = 0; i < 14; i++)
            {
                var k = t - i * .043f - (i % 3) * .008f;
                if (k >= 0 && k < .3f)
                    value += (Bell(k, 1400 + (i * 137) % 1300) * .45f + Noise(k) * Mathf.Exp(-k * 80) * .24f) / (1 + i * .04f);
            }
            return value;
        }
        private static float Fanfare(float t, bool jackpot)
        {
            var notes = jackpot ? JackpotNotes : WinNotes;
            var step = jackpot ? .17f : .14f;
            var value = 0f;
            for (var i = 0; i < notes.Length; i++)
                value += Bell(t - i * step, notes[i]) * .5f;
            var chordAt = (notes.Length - 1) * step;
            value += Bell(t - chordAt, 523.251f) * .35f + Bell(t - chordAt, 659.255f) * .3f + Bell(t - chordAt, 783.991f) * .3f;
            if (jackpot)
                value += Bell(t, 261.626f) * .22f + Bell(t - .06f, 523.251f) * .18f + Payout(t - 1.8f) * .3f;
            return value;
        }
        private void ClearBank()
        {
            _effects.Stop();
            _nativeShot.Release();
            _motor.Stop();
            _ambient.Stop();
            foreach (var clip in _cues.Values)
                if (clip != null)
                    Object.Destroy(clip);
            _cues.Clear();
            if (_rolling != null)
                Object.Destroy(_rolling);
            if (_hum != null)
                Object.Destroy(_hum);
            _rolling = _hum = null;
        }
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
            ClearBank();
            if (_root != null)
                Object.Destroy(_root);
        }
    }
}
