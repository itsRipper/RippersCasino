using System;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using UnityEngine;

namespace RippersCasino.Client
{
    internal sealed class NativeRevolverShot
    {
        private const string Bundle = "assets/content/audio/banks/ash12.bundle";
        private const string Bank = "rsh_12_main";
        private readonly Action<string> _log;
        private IEasyAssets _assets;
        private DependencyGraph<IEasyBundle>.Token _token;
        private Task _loading;
        private AudioClip _clip;
        private float _deadline;
        private bool _failed, _announced;
        internal NativeRevolverShot(Action<string> log) { _log = log; }

        internal void Prepare()
        {
            if (_failed) return;
            try
            {
                var assets = Singleton<ObjectsFactory>.Instance?.EasyAssets;
                if (assets == null) return;
                if (_assets != null && !ReferenceEquals(_assets, assets)) Release();
                if (_token == null)
                {
                    _assets = assets;
                    _token = assets.Retain(new[] { Bundle }, null, CancellationToken.None);
                    _loading = _token.LoadingJob;
                    _deadline = Time.unscaledTime + 10;
                }
                if (!_loading.IsCompleted)
                {
                    if (Time.unscaledTime >= _deadline) Fail("audio bank loading timed out");
                    return;
                }
                _loading.GetAwaiter().GetResult();
                if (_clip == null)
                {
                    var bank = _assets.GetAsset<SoundBank>(Bundle, Bank);
                    if (bank == null || bank.name != Bank) throw new InvalidOperationException("RSh-12 bank unavailable");
                    _clip = bank.PickSingleClip((int)EnvironmentType.Indoor);
                    if (_clip == null || _clip.name != "rsh_12_indoor_close_oneshot")
                        throw new InvalidOperationException("RSh-12 indoor one-shot unavailable");
                }
                if (_clip.loadState == AudioDataLoadState.Unloaded && !_clip.LoadAudioData())
                    throw new InvalidOperationException("RSh-12 audio data could not load");
                if (_clip.loadState == AudioDataLoadState.Failed)
                    throw new InvalidOperationException("RSh-12 audio data failed");
                if (_clip.loadState != AudioDataLoadState.Loaded)
                {
                    if (Time.unscaledTime >= _deadline) Fail("audio data loading timed out");
                    return;
                }
                if (!_announced)
                {
                    _announced = true;
                    _log?.Invoke("[RippersCasino] Native RSh-12 audio ready.");
                }
            }
            catch (Exception error) { Fail(error.GetBaseException().Message); }
        }

        internal bool TryPlay(AudioSource source)
        {
            if (_clip == null || _clip.loadState != AudioDataLoadState.Loaded) return false;
            source.PlayOneShot(_clip);
            return true;
        }

        private void Fail(string reason)
        {
            Release();
            _failed = true;
            _log?.Invoke("[RippersCasino] Native RSh-12 audio unavailable; gunshot audio disabled for this session. " + reason);
        }

        internal void Release()
        {
            _clip = null;
            _assets = null;
            _announced = _failed = false;
            if (_loading != null)
            {
                if (_loading.IsFaulted) { var ignored = _loading.Exception; }
                else if (!_loading.IsCompleted)
                    _loading.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                _loading = null;
            }
            var token = _token;
            _token = null;
            token?.Release();
        }
    }
}
