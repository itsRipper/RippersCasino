using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RippersCasino.Client
{
    // A temporary virtual focus on Tarkov's existing brain/output camera. No
    // replacement camera, listener, player teleport or persistent scene edits.
    internal sealed class LaptopSession : IDisposable
    {
        private const string FocusName = "RippersCasino.LaptopFocus";
        private const float BlendTime = .8f;
        private readonly Action<string> _log;
        private readonly Func<bool> _diagnostics;
        private int _frames, _gcStart;
        private float _frameTotal, _maxFrame;
        private CinemachineBrain _brain;
        private CinemachineVirtualCamera _focus;
        private CinemachineBlenderSettings _originalBlends, _sessionBlends;
        private readonly List<UiState> _ui = new List<UiState>();
        private float _entered, _leaving = -1;
        internal bool Holding { get; private set; }
        internal float Opacity => !Holding ? 0 : _leaving >= 0 ? 1 - Mathf.SmoothStep(0, 1, (Time.unscaledTime - _leaving) / .18f) : Mathf.SmoothStep(0, 1, (Time.unscaledTime - _entered - .65f) / .4f);
        internal bool Ready => Holding && _leaving < 0 && Time.unscaledTime - _entered >= 1.05f;
        private sealed class UiState
        {
            internal CanvasGroup Group; internal float Alpha; internal bool Interactable, Blocks, Added;
        }
        internal LaptopSession(Action<string> log, Func<bool> diagnostics)
        {
            _log = log;
            _diagnostics = diagnostics;
        }
        internal void Begin(Transform laptop, Camera camera, params GameObject[] nativeUi)
        {
            Dispose();
            Holding = true;
            _entered = Time.unscaledTime;
            _leaving = -1;
            _frames = 0;
            _frameTotal = _maxFrame = 0;
            _gcStart = _diagnostics() ? GC.CollectionCount(0) : 0;
            foreach (var node in nativeUi)
            {
                if (node == null)
                    continue;
                var group = node.GetComponent<CanvasGroup>();
                var added = group == null;
                if (added)
                    group = node.AddComponent<CanvasGroup>();
                _ui.Add(new UiState { Group = group, Alpha = group.alpha, Interactable = group.interactable, Blocks = group.blocksRaycasts, Added = added });
                group.alpha = 0;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            if (laptop == null || camera == null)
            {
                _log?.Invoke("[RippersCasino] Laptop focus unavailable; using display wake only.");
                return;
            }
            _brain = camera.GetComponent<CinemachineBrain>();
            if (_brain == null)
                foreach (var brain in Object.FindObjectsOfType<CinemachineBrain>())
                    if (brain.isActiveAndEnabled && brain.OutputCamera == camera)
                    {
                        _brain = brain;
                        break;
                    }
            if (_brain == null)
            {
                _log?.Invoke("[RippersCasino] Native Cinemachine brain unavailable; using display wake only.");
                return;
            }
            // The installed open-laptop prefab has its hinge at the root. Prefer
            // a named display renderer where supplied by another compatible model.
            var aim = laptop.position + Vector3.up * .14f;
            Renderer display = null, quad = null;
            foreach (var renderer in laptop.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    display = renderer;
                    break;
                }
                if (renderer.name.Equals("Quad", StringComparison.OrdinalIgnoreCase))
                    quad = renderer;
            }
            if (display == null)
                display = quad;
            var front = Vector3.ProjectOnPlane(camera.transform.position - aim, Vector3.up).normalized;
            if (display != null)
            {
                aim = display.bounds.center;
                // Unity's screen quad faces local -Z. Use readable mesh normals
                // where available, including transformed custom laptop displays.
                var mesh = display.GetComponent<MeshFilter>()?.sharedMesh;
                var normal = Vector3.back;
                if (mesh != null && mesh.isReadable)
                {
                    var normals = mesh.normals;
                    if (normals.Length > 0)
                        normal = normals[0];
                }
                front = display.transform.TransformDirection(normal).normalized;
                _log?.Invoke("[RippersCasino] Display focus surface=" + display.name + " center=" + aim + " outward=" + front);
            }
            if (front.sqrMagnitude < .01f)
                front = -laptop.forward;
            var eye = aim + front * .30f;
            var rotation = Quaternion.LookRotation(aim - eye, Vector3.up);
            _originalBlends = _brain.m_CustomBlends;
            _sessionBlends = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
            var original = _originalBlends != null ? _originalBlends.m_CustomBlends : null;
            var blends = new CinemachineBlenderSettings.CustomBlend[2 + (original?.Length ?? 0)];
            var ease = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut, BlendTime);
            blends[0] = new CinemachineBlenderSettings.CustomBlend { m_From = CinemachineBlenderSettings.kBlendFromAnyCameraLabel, m_To = FocusName, m_Blend = ease };
            blends[1] = new CinemachineBlenderSettings.CustomBlend { m_From = FocusName, m_To = CinemachineBlenderSettings.kBlendFromAnyCameraLabel, m_Blend = ease };
            if (original != null)
                Array.Copy(original, 0, blends, 2, original.Length);
            _sessionBlends.m_CustomBlends = blends;
            _brain.m_CustomBlends = _sessionBlends;
            var nodeFocus = new GameObject(FocusName);
            nodeFocus.SetActive(false);
            _focus = nodeFocus.AddComponent<CinemachineVirtualCamera>();
            _focus.transform.SetPositionAndRotation(eye, rotation);
            _focus.m_Lens = LensSettings.FromCamera(camera);
            _focus.m_Lens.FieldOfView = 48;
            _focus.m_Lens.NearClipPlane = .025f;
            _focus.Priority = int.MaxValue - 1;
            nodeFocus.SetActive(true);
            _log?.Invoke("[RippersCasino] Native laptop focus entered: eye=" + eye + " aim=" + aim + " previous=" + _brain.ActiveVirtualCamera?.Name + " blend=.8s.");
        }
        internal void Leave()
        {
            if (!Holding || _leaving >= 0)
                return;
            _leaving = Time.unscaledTime;
            if (_focus != null)
                _focus.Priority = int.MinValue;
        }
        internal void Tick()
        {
            if (_diagnostics() && Ready)
            {
                _frames++;
                _frameTotal += Time.unscaledDeltaTime;
                _maxFrame = Mathf.Max(_maxFrame, Time.unscaledDeltaTime);
            }
            if (Holding && _leaving >= 0 && Time.unscaledTime - _leaving >= BlendTime + .05f)
                Dispose();
        }
        public void Dispose()
        {
            if (_brain != null && _brain.m_CustomBlends == _sessionBlends)
                _brain.m_CustomBlends = _originalBlends;
            if (_focus != null)
            {
                _focus.enabled = false;
                Object.Destroy(_focus.gameObject);
            }
            _focus = null;
            if (_sessionBlends != null)
                Object.Destroy(_sessionBlends);
            _sessionBlends = null;
            _brain = null;
            foreach (var saved in _ui)
            {
                if (saved.Group == null)
                    continue;
                saved.Group.alpha = saved.Alpha;
                saved.Group.interactable = saved.Interactable;
                saved.Group.blocksRaycasts = saved.Blocks;
                if (saved.Added)
                    Object.Destroy(saved.Group);
            }
            _ui.Clear();
            if (_diagnostics() && Holding && _frames > 0)
                _log?.Invoke("[RippersCasino] Session frame sample: frames=" + _frames + " meanMs=" + (_frameTotal * 1000 / _frames).ToString("F2") + " maxMs=" + (_maxFrame * 1000).ToString("F2") + " processGen0Collections=" + (GC.CollectionCount(0) - _gcStart));
            if (Holding)
                _log?.Invoke("[RippersCasino] Laptop focus released; native camera, UI and controls restored.");
            Holding = false;
            _leaving = -1;
        }
    }
}
