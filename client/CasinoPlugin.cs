using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InputSystem;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RippersCasino.Client
{
    [BepInPlugin("com.itsripper.ripperscasino", "itsRipper-RippersCasino", "1.0.0")]
    public sealed partial class CasinoPlugin : BaseUnityPlugin
    {
        private static CasinoPlugin _instance;
        private Harmony _patches;
        private ConfigEntry<float> _volume, _effectsVolume, _reelsVolume, _ambientReduction;
        private ConfigEntry<bool> _audioMuted, _audioDiagnostics, _slowReels, _presentationDiagnostics;
        private float _spinClockRate = 1;
        private bool _welcomePending;
        private string _heardConnectionError;
        private double PresentationElapsed => (_presentation.Resolved ? Time.unscaledTime - _resolveAt : Time.unscaledTime - _waitingAt) * _spinClockRate;
        private HideoutPlayerOwner _owner;
        private float _scanAt, _suppressUntil, _resolveAt, _waitingAt;
        private bool _open, _busy, _restart, _pendingFromDisk, _requestSent;
        private CasinoState _state;
        private CasinoRequest _pending;
        private string _pendingPath, _message = "Open Rippers Casino from the hideout laptop.", _depositQuantity = "100000", _withdrawQuantity = "10";
        private int _currency, _bet, _lastStop = -1, _guiGroups;
        private float _celebrateAt = -100;
        private bool _jackpotReveal, _jackpotOutcome;
        private bool JackpotCelebrating => _jackpotReveal && _presentation.Revealed && Time.unscaledTime - _celebrateAt < JackpotPresentation.Duration;
        private readonly string[] _currencies = { "RUB", "USD", "EUR" };
        private readonly SpinPresentation _presentation = new SpinPresentation();
        private long _shownBalance;
        private readonly int[] _restSymbols = { 0, 2, 3 };
        private GameObject _blocker;
        private CasinoAudio _casinoAudio;
        private LaptopSession _laptopSession;
        private HideoutAudioFocus _audioFocus;

        private GUIStyle _text, _small, _button, _center;
        private CabinetLettering _lettering;
        private Vector2 _guiOrigin;
        private Rect _guiClip = new Rect(-100000, -100000, 200000, 200000);
        private readonly System.Collections.Generic.Stack<Rect> _guiClips = new System.Collections.Generic.Stack<Rect>();
        private readonly System.Collections.Generic.Stack<Vector2> _guiOrigins = new System.Collections.Generic.Stack<Vector2>();
        private CursorLockMode _oldLock;
        private bool _oldCursor;
        private bool Mask => _restart || _open || (_laptopSession?.Holding ?? false) || _busy || Time.unscaledTime < _suppressUntil;
        private bool Connected => ItemUiContext.Instance != null && ItemUiContext.Instance.ClientSession != null;
        private bool InHideout => HideoutScreenVisible && _owner != null && _owner.gameObject.activeInHierarchy && _owner.HideoutPlayer != null && _owner.FirstPersonMode && !_owner.InShootingRange && !_owner.IsGuest && !_owner.HideoutPlayer.IsUpdateHideoutPlayerInventoryInProgress && Connected;
        private bool HideoutScreenVisible
        {
            get
            {
                var ui = CommonUI.Instance;
                if (ui == null)
                    return false;
                return ui.HideoutScreenRear != null && ui.HideoutScreenRear.gameObject.activeInHierarchy
                    && !(ui.InventoryScreen != null && ui.InventoryScreen.gameObject.activeInHierarchy)
                    && !(ui.SettingsScreen != null && ui.SettingsScreen.gameObject.activeInHierarchy)
                    && !(ui.WeaponModdingScreen != null && ui.WeaponModdingScreen.gameObject.activeInHierarchy)
                    && !(ui.HandbookScreen != null && ui.HandbookScreen.gameObject.activeInHierarchy)
                    && !(ui.ChatScreen != null && ui.ChatScreen.gameObject.activeInHierarchy)
                    && !(ui.TraderDialogScreen != null && ui.TraderDialogScreen.gameObject.activeInHierarchy);
            }
        }
        private bool Overview => HideoutScreenVisible && !CommonUI.Instance.HideoutScreenRear.FirstPerson && Connected;
        private bool CanUse => InHideout || Overview;
        private bool Available => _open && (_laptopSession?.Ready ?? true) && !_restart && !_busy && !GameActive && !JackpotCelebrating && _state?.enabled == true && _state.available && _state.economyVersion == 4 && _pending == null && !_tableSending && _tablePending == null;

        private bool _disposed;
        private void Awake()
        {
            try { Initialize(); }
            catch (Exception error)
            {
                Logger.LogError("[RippersCasino] Initialization stopped; recovery files preserved: " + error);
                enabled = false;
                DisposePlugin();
            }
        }
        private void Initialize()
        {
            _instance = this;
            _laptopAction.Action = Open;
            _presentationDiagnostics = Config.Bind("Diagnostics", "Presentation", false, "Collect frame intervals and laptop diagnostics for troubleshooting.");
            _volume = Config.Bind("Audio", "Volume", .65f, new ConfigDescription("Independent casino volume.", new AcceptableValueRange<float>(0, 1)));
            _effectsVolume = Config.Bind("Audio", "EffectsVolume", 1f, new ConfigDescription("Volume for buttons, wins and the cashier.", new AcceptableValueRange<float>(0, 1)));
            _reelsVolume = Config.Bind("Audio", "ReelsVolume", 1f, new ConfigDescription("Volume for spinning reels.", new AcceptableValueRange<float>(0, 1)));
            _ambientReduction = Config.Bind("Audio", "HideoutAttenuationDb", 20f, new ConfigDescription("Quietens hideout ambience while the casino is open.", new AcceptableValueRange<float>(0, 40)));
            _audioMuted = Config.Bind("Audio", "Muted", false, "Mute casino audio without losing volume preferences.");
            _audioDiagnostics = Config.Bind("Diagnostics", "AudioOutput", false, "Log playback output probes on entry/spin for troubleshooting.");
            _slowReels = Config.Bind("Diagnostics", "SlowReels", false, "Present slots and races at one fifth speed for interactive interruption testing. Does not change outcomes or transactions.");
            _patches = new Harmony("com.itsripper.ripperscasino.input");
            foreach (var type in new[] { typeof(MenuScreen), typeof(HideoutPlayerOwner), typeof(HideoutScreenOverlay), typeof(HideoutScreenRear) })
                Patch(type, "TranslateCommand", nameof(BlockCommand));
            Patch(typeof(MenuScreen), "Click", nameof(BlockClick));
            Patch(typeof(PlayerOwner), "TranslateAxes", nameof(BlockPlayerAxes));
            foreach (var type in new[] { typeof(HideoutScreenOverlay), typeof(HideoutScreenRear) })
            {
                Patch(type, "TranslateAxes", nameof(BlockAxes));
                Patch(type, "ShouldLockCursor", nameof(ShowCursor));
            }
            try
            {
                LoadPending();
                LoadTablePending();
            }
            catch (Exception ex) { throw new InvalidDataException("Recovery data unreadable; original files preserved.", ex); }
            _casinoAudio = new CasinoAudio(gameObject, () => _audioMuted.Value ? 0 : _volume.Value, () => _effectsVolume.Value, () => _reelsVolume.Value, () => _audioDiagnostics.Value, message => Logger.LogInfo(message));
            _laptopSession = new LaptopSession(message => { if (_presentationDiagnostics.Value) Logger.LogInfo(message); }, () => _presentationDiagnostics.Value);
            _audioFocus = new HideoutAudioFocus(message => { if (_audioDiagnostics.Value) Logger.LogInfo(message); });
            LoadModuleIcon();
            LoadCasinoAssets();
            IntelligenceCenterAccess.Install(_patches);

            Logger.LogInfo("[RippersCasino] Ready. Use the existing hideout laptop or the Intelligence Center Rippers Casino interaction.");
        }
        private void Patch(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            if (target == null)
                throw new MissingMethodException(type.FullName, method);
            _patches.Patch(target, prefix: new HarmonyMethod(typeof(CasinoPlugin), prefix));
        }
        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Logger.LogError("[RippersCasino] UI exception: " + ex);
                if (_pending != null && _requestSent)
                    RequireRestart("Transaction interrupted. Restart Tarkov to reload your stash.");
                _busy = false;
                _casinoAudio?.StopSpin();
                _presentation.Reject(_shownBalance);
                _racePresentation.Reject(_shownBalance);
                Close(true);
            }
        }
        private void Tick()
        {
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 1;
                _owner = UnityEngine.Object.FindObjectOfType<HideoutPlayerOwner>();
            }
            _laptopSession?.Tick();
            SamplePresentation();
            var connectionError = _tableError ?? _state?.error;
            if (_open && connectionError != null && connectionError != _heardConnectionError)
                Play(CasinoCue.Error);
            _heardConnectionError = connectionError;
            if (_revolverTrigger.TryFire(Time.unscaledTime, out var triggerSound))
            {
                if (_open && _page == 8 && triggerSound != RevolverSound.None)
                {
                    Play(triggerSound == RevolverSound.Gunshot ? CasinoCue.Gunshot : CasinoCue.SafeClick);
                    if (_audioDiagnostics.Value) Logger.LogInfo("[RippersCasino] Revolver trigger / " + triggerSound + " / sequence=" + _eliminationSequence);
                }
                var match = _tables?.table?.elimination;
                if (match?.phase == "complete")
                {
                    _heardEliminationWinner = match.id;
                    if (_tables.table.seats.Any(s => s.mine && s.position == match.winner)) _eliminationWinAt = Time.unscaledTime + .3f;
                }
            }
            if (_eliminationWinAt >= 0 && Time.unscaledTime >= _eliminationWinAt)
            {
                _eliminationWinAt = -1;
                if (_open && _page == 8) Play(CasinoCue.Win);
            }
            _audioFocus?.Tick(_open && HideoutScreenVisible, _ambientReduction.Value, Time.unscaledDeltaTime);
            if (!HideoutScreenVisible)
                _audioFocus?.Restore();
            if (!_open && !(_laptopSession?.Holding ?? false) && _blocker != null)
                FinishClose();
            TickModule();
            TickTableSession();
            TickRace();
            if (_open && _welcomePending && (_laptopSession?.Opacity ?? 1) > 0)
            {
                _welcomePending = false;
                Play(CasinoCue.Open);
            }
            if ((_open || (_laptopSession?.Holding ?? false)) && (!CanUse || (_laptop != null && !_laptop.gameObject.activeInHierarchy)))
                Close(true);
            if (_open && Input.GetKeyDown(KeyCode.Escape))
                Close();
            if (_open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (_presentation.Active)
            {
                var elapsed = PresentationElapsed;
                if (_presentation.Resolved)
                    for (var i = 0; i < 3; i++)
                        if (_presentation.Stopped(i, elapsed) && _lastStop < i)
                        {
                            _lastStop = i;
                            Play(CasinoCue.ReelStop);
                        }
                var reveal = _presentation.Tick(elapsed);
                _shownBalance = _presentation.VisibleBalance;
                if (reveal)
                {
                    _shownBalance = _presentation.VisibleBalance;
                    _casinoAudio.StopSpin();
                    _jackpotReveal = _jackpotOutcome;
                    _celebrateAt = Time.unscaledTime;
                    _message = _presentation.VisiblePayout > 0 ? (_jackpotReveal ? "JACKPOT  /  PAID  " : "PAID  ") + _presentation.VisiblePayout.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " CHIPS" : "No payout.";
                    Play(_jackpotReveal ? CasinoCue.Jackpot : _presentation.VisiblePayout > 0 ? _presentation.VisiblePayout < (long)(_state?.bets[_bet] ?? 1) * 5 ? CasinoCue.SmallWin : CasinoCue.Win : CasinoCue.Button);
                    if (_presentation.VisiblePayout > 0)
                        Play(CasinoCue.Payout);
                }
            }
            TickAutoSpin();
        }
        private double ReelPosition(int reel) => _presentation.Position(reel, PresentationElapsed, _state?.symbols.Length ?? 6);
        private void Open()
        {
            if (!CanUse || _open || (_laptopSession?.Holding ?? false))
                return;
            _oldLock = Cursor.lockState;
            _oldCursor = Cursor.visible;
            _open = true;
            try
            {
                CreateInputBlocker();
                var ui = CommonUI.Instance;
                _laptopSession.Begin(_laptop, ActiveCamera, ui.HideoutScreenRear.gameObject, ui.HideoutScreenOverlay.gameObject);
                _welcomePending = true;
                if (!_busy && !GameActive && !_restart)
                    StartCoroutine(Guard(Refresh()));
            }
            catch (Exception ex) { Logger.LogError("[RippersCasino] Laptop entry recovered: " + ex); Close(true); }
        }
        private void CreateInputBlocker()
        {
            _blocker = new GameObject("RippersCasino.InputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = _blocker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            var cover = new GameObject("Cover", typeof(RectTransform), typeof(Image));
            cover.transform.SetParent(_blocker.transform, false);
            var rect = cover.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            cover.GetComponent<Image>().color = Color.clear;
        }
        private void Close(bool force = false)
        {
            _autoSpin.Stop();
            ReportPresentation();
            if (!_open && !(_laptopSession?.Holding ?? false))
                return;
            if (_audioDiagnostics?.Value == true && GameActive)
                Logger.LogInfo("[RippersCasino] Closing active " + (_racePresentation.Active ? "race" : "spin") + " presentation; resolved=" + (_racePresentation.Active ? _racePresentation.Resolved : _presentation.Resolved) + " elapsed=" + (_racePresentation.Active ? RaceElapsed : PresentationElapsed) + "; receipt retained, no new transaction.");
            _welcomePending = false;
            _open = false;
            _suppressUntil = Time.unscaledTime + .25f;
            _casinoAudio?.StopSpin();
            if (force)
            {
                if (_rouletteWheel != null)
                    Destroy(_rouletteWheel);
                if (_feltMask != null)
                    Destroy(_feltMask);
                _laptopSession?.Dispose();
                _audioFocus?.Restore();
                FinishClose();
            }
            else
                _laptopSession?.Leave();
        }
        private void FinishClose()
        {
            if (_blocker != null)
                Destroy(_blocker);
            _blocker = null;
            Cursor.lockState = _oldLock;
            Cursor.visible = _oldCursor;
        }
        private static bool BlockClick() => _instance == null || !_instance.Mask;
        private static bool BlockCommand(ref InputNode.ETranslateResult __result)
        {
            if (_instance?._open == true && Input.GetKeyDown(KeyCode.Escape)) _instance.Close();
            if (BlockClick())
                return true;
            __result = InputNode.ETranslateResult.BlockAll;
            return false;
        }
        private static bool BlockAxes(ref float[] __0)
        {
            if (BlockClick())
                return true;
            __0 = null;
            return false;
        }
        private static bool BlockPlayerAxes(PlayerOwner __instance, ref float[] __0) => !(__instance is HideoutPlayerOwner) || BlockAxes(ref __0);
        private static bool ShowCursor(ref ECursorResult __result)
        {
            if (_instance == null || (!_instance._open && !(_instance._laptopSession?.Holding ?? false)))
                return true;
            __result = ECursorResult.ShowCursor;
            return false;
        }
        private void Play(CasinoCue cue)
        {
            if (_open)
                _casinoAudio?.Play(cue);
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            _busy = false;
            _stateLoading = false;
            _tablePolling = _tableSending = false;
            if (_pending != null && _requestSent)
            {
                _restart = true;
                _message = "Payment interrupted. Restart Tarkov to refresh your stash.";
            }
            Close(true);
            ClearLaptopMenu();
            if (_blocker != null)
                FinishClose();
            _audioFocus?.Restore();
            _casinoAudio?.Suspend();
        }
        private void OnDestroy() => DisposePlugin();
        private void DisposePlugin()
        {
            if (_disposed) return;
            _disposed = true;
            Close(true);
            if (_rouletteWheel != null)
                Destroy(_rouletteWheel);
            if (_feltMask != null)
                Destroy(_feltMask);
            _laptopSession?.Dispose();
            _audioFocus?.Dispose();
            _patches?.UnpatchSelf();
            _casinoAudio?.Dispose();
            DisposeModule();
            if (_chipIcon != null) Destroy(_chipIcon);
            _lettering?.Dispose();
            if (_backroomTable != null) Destroy(_backroomTable);
            _revolverArtwork?.Dispose();
            if (_revolverArt != null) Destroy(_revolverArt);
            if (_muzzleFlash != null) Destroy(_muzzleFlash);
            if (_slotCabinet != null) Destroy(_slotCabinet);
            if (_cardAtlas != null) Destroy(_cardAtlas);
            if (_tableArt != null) Destroy(_tableArt);
            if (_portraitAtlas != null) Destroy(_portraitAtlas);
            _instance = null;
        }

    }
}
