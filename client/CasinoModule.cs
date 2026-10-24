using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EFT;
using EFT.Hideout;
using EFT.UI;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private static readonly string[] LaptopNames = { "laptop", "notebook" };
        private static readonly string[] LaptopCandidates = { "laptop", "notebook", "mrkerman", "mr_kerman" };
        private readonly RaycastHit[] _laptopHits = new RaycastHit[32];
        private readonly HashSet<Stage> _registeredStages = new HashSet<Stage>();
        private Texture2D _moduleIcon;
        private Transform _laptop;
        private float _modulePollAt, _laptopProbeAt;
        private bool _stateLoading, _laptopLogged;
        private HideoutPlayerOwner _menuOwner;
        private AreaData _intelArea;
        private HideoutArea _intelScene;
        private readonly InteractionAction _laptopAction = new InteractionAction { Name = "Rippers Casino" };
        internal static void RegisterLaptop(AreaTemplate template)
        {
            if (_instance == null)
                return;
            foreach (var entry in template.Stages)
            {
                if (entry.Key < 1)
                    continue;
                RegisterLaptopStage(entry.Value);
            }
        }
        internal static void RegisterLaptopStage(Stage stage)
        {
            if (_instance == null || stage == null)
                return;
            if (stage.Interactions == null)
                stage.Interactions = new RelatedInteractions();
            if (stage.Interactions.Data == null)
                stage.Interactions.Data = new AvailableInteractionState();
            if (!stage.Interactions.Data.Actions.Any(a => a.Name == "Rippers Casino"))
            {
                stage.Interactions.Data.Actions.Add(_instance._laptopAction);
                _instance._registeredStages.Add(stage);
            }
        }
        private Camera ActiveCamera => EFT.CameraControl.CameraManager.Exist ? EFT.CameraControl.CameraManager.Instance.Camera : Camera.main;
        private void LoadModuleIcon()
        {
            var path = Path.Combine(Path.GetDirectoryName(Info.Location), "assets", "casino-icon.png");
            _moduleIcon = new Texture2D(2, 2);
            ImageConversion.LoadImage(_moduleIcon, File.ReadAllBytes(path), true);
        }
        private void TickModule()
        {
            _casinoAudio?.Tick(HideoutScreenVisible, _open, _presentation.Active && !_presentation.Revealed);
            if (!HideoutScreenVisible)
            {
                ClearLaptopMenu();
                return;
            }
            if (!_busy && !GameActive && !_stateLoading && Time.unscaledTime >= _modulePollAt && !_open)
            {
                _modulePollAt = Time.unscaledTime + 8;
                StartCoroutine(Guard(PollModule()));
            }
            if (_owner == null || _owner.IsGuest)
            {
                ClearLaptopMenu();
                return;
            }
            if (_intelScene == null)
            {
                var area = Resources.FindObjectsOfTypeAll<HideoutArea>().FirstOrDefault(a => a.gameObject.scene.IsValid() && a.Data?.Template.Type == EAreaType.IntelligenceCenter);
                if (area != null)
                {
                    _intelScene = area;
                    _intelArea = area.Data;
                    RegisterLaptop(_intelArea.Template);
                }
            }
            if (_laptop == null && Time.unscaledTime >= _laptopProbeAt)
            {
                _laptopProbeAt = Time.unscaledTime + 3;
                var candidates = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && LaptopCandidates.Any(s => t.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                if (!_laptopLogged)
                {
                    _laptopLogged = true;
                    if (_presentationDiagnostics.Value) Logger.LogInfo("[RippersCasino] Existing laptop candidates: " + string.Join(" | ", candidates.Take(20).Select(t => t.name + " @ " + t.position + " parent " + t.parent?.name)));
                }
                var candidate = candidates.Where(t => t.GetComponentInChildren<Renderer>() != null && t.name.IndexOf("closed", StringComparison.OrdinalIgnoreCase) < 0 && t.name.IndexOf("wires", StringComparison.OrdinalIgnoreCase) < 0).OrderBy(t => _intelScene != null && t.IsChildOf(_intelScene.transform) ? 0 : 1).ThenBy(t => _intelArea?.AreaIconPoint != null ? Vector3.Distance(t.position, _intelArea.AreaIconPoint.position) : 0).FirstOrDefault();
                if (candidate != null)
                {
                    _laptop = candidate;
                    while (_laptop.parent != null && LaptopNames.Any(s => _laptop.parent.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        _laptop = _laptop.parent;
                    if (_presentationDiagnostics.Value) Logger.LogInfo("[RippersCasino] Reusing installed laptop model: " + _laptop.name + " @ " + _laptop.position + " Intel level " + _intelArea?.CurrentLevel + " interactions " + _intelArea?.CurrentStage?.Interactions?.Data?.Actions.Count);
                }
            }
            if (!InHideout || _open || _laptop == null || _state?.available != true)
            {
                ClearLaptopMenu();
                return;
            }
            var camera = ActiveCamera;
            if (camera == null)
            {
                ClearLaptopMenu();
                return;
            }
            var player = ((Component)_owner.HideoutPlayer).transform;
            RaycastHit? nearest = null;
            var ray = camera.ScreenPointToRay(new Vector3(Screen.width * .5f, Screen.height * .5f));
            var count = Physics.RaycastNonAlloc(ray, _laptopHits, 3, ~0, QueryTriggerInteraction.Ignore);
            // A full buffer may omit the nearest valid hit; preserve the complete fallback.
            var hits = count == _laptopHits.Length ? Physics.RaycastAll(ray, 3, ~0, QueryTriggerInteraction.Ignore) : _laptopHits;
            if (hits != _laptopHits) count = hits.Length;
            for (var index = 0; index < count; index++)
            {
                var hit = hits[index];
                if (hit.collider.transform.IsChildOf(player))
                    continue;
                if (!nearest.HasValue || hit.distance < nearest.Value.distance)
                    nearest = hit;
            }
            if (!nearest.HasValue || (!nearest.Value.collider.transform.IsChildOf(_laptop) && nearest.Value.collider.transform != _laptop))
            {
                ClearLaptopMenu();
                return;
            }
            _menuOwner = _owner;
            var current = _owner.AvailableInteractionState.Value;
            if (current == null || !current.Actions.Contains(_laptopAction))
            {
                var menu = new AvailableInteractionState();
                if (current?.Actions != null)
                    menu.Actions.AddRange(current.Actions.Where(action => action.Name != "Rippers Casino"));
                menu.Actions.Add(_laptopAction);
                menu.InitSelected();
                _owner.AvailableInteractionState.Value = menu;
            }
        }
        private void ClearLaptopMenu()
        {
            if (_menuOwner != null)
            {
                var current = _menuOwner.AvailableInteractionState.Value;
                if (current?.Actions?.Any(a => a.Name == "Rippers Casino") == true)
                {
                    var menu = new AvailableInteractionState();
                    menu.Actions.AddRange(current.Actions.Where(a => a.Name != "Rippers Casino"));
                    menu.InitSelected();
                    _menuOwner.AvailableInteractionState.Value = menu;
                }
            }
            _menuOwner = null;
        }
        private System.Collections.IEnumerator PollModule()
        {
            _stateLoading = true;
            try
            {
                var request = SPT.Common.Http.RequestHandler.GetJsonAsync("/ripperscasino/state");
                var deadline = Time.unscaledTime + 20;
                while (!request.IsCompleted && Time.unscaledTime < deadline)
                    yield return null;
                if (request.IsCompleted && !_busy && !GameActive)
                    ReadState(request);
            }
            finally
            {
                _stateLoading = false;
            }
        }
        private void DisposeModule()
        {
            ClearLaptopMenu();
            foreach (var stage in _registeredStages)
                stage.Interactions?.Data?.Actions?.Remove(_laptopAction);
            _registeredStages.Clear();
            if (_moduleIcon != null)
                Destroy(_moduleIcon);
            foreach (var texture in _symbols)
                if (texture != null)
                    Destroy(texture);
            foreach (var texture in _factoryLayers)
                if (texture != null)
                    Destroy(texture);
            foreach (var texture in _runnerSheets)
                if (texture != null)
                    Destroy(texture);
        }
    }
}
