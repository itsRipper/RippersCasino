using System;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private int _pressedButton, _hoverButton, _nextHoverButton;
        private void BeginGroup(Rect area)
        {
            GUI.BeginGroup(area);
            _guiGroups++;
            _guiOrigins.Push(_guiOrigin);
            _guiClips.Push(_guiClip);
            _guiOrigin += area.position;
            _guiClip = Intersection(_guiClip, new Rect(_guiOrigin, area.size));
        }
        private void EndGroup()
        {
            GUI.EndGroup();
            _guiGroups--;
            _guiOrigin = _guiOrigins.Pop();
            _guiClip = _guiClips.Pop();
        }
        private void Styles()
        {
            if (_text != null)
                return;
            _lettering = new CabinetLettering();
            _text = new GUIStyle(GUI.skin.label) { fontSize = Ui.BodySize, wordWrap = true };
            _text.normal.textColor = new Color(.85f, .86f, .88f);
            _small = new GUIStyle(_text) { fontSize = Ui.MetaSize };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _center = new GUIStyle(_text) { alignment = TextAnchor.MiddleCenter };
        }
        private void Label(Rect area, string text, GUIStyle style = null)
        {
            Styles();
            var position = GUI.matrix.MultiplyPoint3x4(area.position + _guiOrigin);
            var size = GUI.matrix.MultiplyVector(area.size);
            _lettering.Draw(area, new Rect(position.x, position.y, size.x, size.y), text, style ?? _text);
        }
        private bool Button(Rect area, string text, GUIStyle style = null, bool primary = false, bool selected = false)
        {
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && (!_open || (_laptopSession?.Ready ?? true));
            var control = GUIUtility.GetControlID(FocusType.Passive);
            var hovered = GUI.enabled && area.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.MouseDown && hovered && Event.current.button == 0)
                _pressedButton = control;
            if (hovered && Event.current.type == EventType.Repaint) _nextHoverButton = control;
            var pressed = hovered && _pressedButton == control && Input.GetMouseButton(0);
            var tint = GUI.backgroundColor * (GUI.enabled ? Color.white : new Color(.55f, .55f, .55f, 1));
            var face = primary ? new Color(.39f, .32f, .21f) : selected ? new Color(.20f, .19f, .14f) : new Color(.09f, .105f, .108f);
            if (hovered) face += new Color(.035f, .03f, .02f, 0);
            if (pressed) face *= new Color(.75f, .75f, .75f, 1);
            Fill(new Rect(area.x, area.y + 2, area.width, area.height), new Color(0, 0, 0, .23f));
            Fill(area, face * tint);
            Outline(area, (hovered || selected || primary ? _gold * .72f : _line * 1.8f) * tint);
            Fill(new Rect(area.x + 1, area.y + 1, area.width - 2, 1), new Color(1, 1, 1, pressed ? .02f : .055f));
            var clicked = GUI.Button(area, string.Empty, GUIStyle.none);
            var caption = new Rect(area.x, area.y + (pressed ? 1 : 0), area.width, area.height);
            Label(caption, text, style ?? _button);
            if (clicked) Play(CasinoCue.Button);
            GUI.enabled = wasEnabled;
            return clicked;
        }
        private int CurrencyButtons(Rect area, int selected)
        {
            for (var i = 0; i < _currencies.Length; i++)
            {
                var width = (area.width - 2 * Ui.ControlGap) / 3;
                if (Button(new Rect(area.x + i * (width + Ui.ControlGap), area.y, width, area.height), _currencies[i], selected: i == selected))
                    selected = i;
            }
            return selected;
        }
        private void DrawFrame()
        {
            if (!_restart && !_open && !(_laptopSession?.Holding ?? false)) return;
            try
            {
                if (_open && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    Close();
                }
                if (Event.current.type == EventType.Repaint)
                {
                    _nextHoverButton = 0;
                    _lettering?.BeginFrame();
                }
                if (_restart)
                {
                    if (_lettering != null)
                        _lettering.Opacity = 1;
                    DrawRecoveryExit();
                    return;
                }
                Draw();
            }
            catch (Exception ex)
            {
                while (_guiGroups > 0)
                    EndGroup();
                GUI.enabled = true;
                GUI.matrix = Matrix4x4.identity;
                Logger.LogError("[RippersCasino] Drawing failed: " + ex.Message);
                if (_pending != null && _requestSent)
                {
                    RequireRestart("Display interrupted during payment. Restart Tarkov.");
                    DrawRecoveryExit();
                }
                else
                    Close(true);
            }
            finally
            {
                if (Event.current.type == EventType.Repaint)
                {
                    if (_nextHoverButton != 0 && _nextHoverButton != _hoverButton) Play(CasinoCue.Hover);
                    _hoverButton = _nextHoverButton;
                    _lettering?.EndFrame();
                }
            }
        }
        private void DrawRecoveryExit()
        {
            GUI.enabled = true;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -200;
            var panel = new Rect(Math.Max(0, (Screen.width - 650) / 2), Math.Max(0, (Screen.height - 260) / 2), 650, 260);
            Fill(panel, new Color(.035f, .045f, .035f));
            Label(new Rect(panel.x + 24, panel.y + 10, 602, 26), "PAYMENT RECOVERY");
            Label(new Rect(panel.x + 24, panel.y + 42, 602, 125), _message + "\n\nYour payment record is protected. Restart Tarkov before using your stash again.");
            if (Button(new Rect(panel.x + 150, panel.y + 192, 350, 45), "QUIT TARKOV"))
                Application.Quit();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        private void Draw()
        {
            if (!_open && !(_laptopSession?.Holding ?? false))
                return;
            Styles();
            GUI.depth = -100;
            if (_open || (_laptopSession?.Holding ?? false))
            {
                _lettering.Opacity = _laptopSession?.Opacity ?? 1;
                var input = GUI.enabled;
                GUI.enabled = input && _open && (_laptopSession?.Ready ?? true);
                DrawCasino();
                GUI.enabled = input;
            }
        }
        private static Rect Intersection(Rect a, Rect b)
        {
            var x = Math.Max(a.x, b.x);
            var y = Math.Max(a.y, b.y);
            return new Rect(x, y, Math.Max(0, Math.Min(a.xMax, b.xMax) - x), Math.Max(0, Math.Min(a.yMax, b.yMax) - y));
        }
        private void DrawTexture(Rect area, Texture texture, ScaleMode scale = ScaleMode.StretchToFill, Rect? source = null, float rotation = 0)
        {
            if (Event.current.type != EventType.Repaint || texture == null)
                return;
            Styles();
            area.position += _guiOrigin;
            if (scale == ScaleMode.ScaleToFit)
            {
                var aspect = texture.width / (float)texture.height;
                if (area.width / area.height > aspect)
                {
                    var width = area.height * aspect;
                    area.x += (area.width - width) / 2;
                    area.width = width;
                }
                else
                {
                    var height = area.width / aspect;
                    area.y += (area.height - height) / 2;
                    area.height = height;
                }
            }
            var clip = Intersection(area, _guiClip);
            if (clip.width <= 0 || clip.height <= 0)
                return;
            var uv = new Rect((clip.x - area.x) / area.width, 1 - (clip.yMax - area.y) / area.height, clip.width / area.width, clip.height / area.height);
            if (source.HasValue)
            {
                var crop = source.Value;
                uv = new Rect(crop.x + uv.x * crop.width, crop.y + uv.y * crop.height, uv.width * crop.width, uv.height * crop.height);
            }
            var position = GUI.matrix.MultiplyPoint3x4(clip.position);
            var size = GUI.matrix.MultiplyVector(clip.size);
            if (texture == Texture2D.whiteTexture)
            {
                var right = Mathf.Round(position.x + size.x);
                var bottom = Mathf.Round(position.y + size.y);
                position.x = Mathf.Round(position.x);
                position.y = Mathf.Round(position.y);
                size.x = Mathf.Max(1, right - position.x);
                size.y = Mathf.Max(1, bottom - position.y);
            }
            _lettering.Paint(new Rect(position.x, position.y, size.x, size.y), texture, GUI.color, uv, rotation);
        }
        private GUIStyle _fieldEditor;
        private string QuantityField(Rect area, string value, int maximumLength = 8)
        {
            if (_fieldEditor == null)
                _fieldEditor = new GUIStyle(GUIStyle.none) { font = _lettering.Font, fontSize = Ui.InputSize, alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(12, 12, 8, 8), clipping = TextClipping.Clip };
            var color = GUI.contentColor;
            var cursorColor = GUI.skin.settings.cursorColor;
            GUI.contentColor = Color.clear;
            GUI.skin.settings.cursorColor = Color.clear;
            var control = "RippersCasinoQuantity:" + _page + ":" + area.x + ":" + area.y;
            GUI.SetNextControlName(control);
            var result = GUI.TextField(area, value, maximumLength, _fieldEditor);
            GUI.skin.settings.cursorColor = cursorColor;
            GUI.contentColor = color;
            Fill(area, new Color(.04f, .052f, .067f));
            var focused = GUI.enabled && GUI.GetNameOfFocusedControl() == control;
            Outline(area, focused ? _gold : _line);
            var editor = focused ? (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) : null;
            var cursor = editor == null ? result.Length : Math.Min(result.Length, editor.cursorIndex);
            var start = 0;
            float Width(string text) => _fieldEditor.CalcSize(new GUIContent(text)).x - _fieldEditor.padding.horizontal;
            while (start < cursor && Width(result.Substring(start, cursor - start)) > area.width - 28) start++;
            var end = result.Length;
            while (end > start && Width(result.Substring(start, end - start)) > area.width - 24) end--;
            var textArea = new Rect(area.x + 12, area.y, area.width - 24, area.height);
            if (focused && editor.selectIndex != editor.cursorIndex)
            {
                var lo = Math.Max(start, Math.Min(cursor, editor.selectIndex));
                var hi = Math.Min(end, Math.Max(cursor, editor.selectIndex));
                if (hi > lo)
                    Fill(new Rect(textArea.x + Width(result.Substring(start, lo - start)), area.center.y - 10, Width(result.Substring(lo, hi - lo)), 20), new Color(.32f, .28f, .19f));
            }
            Label(textArea, result.Substring(start, end - start), _inputLabel ?? _text);
            if (focused && Time.unscaledTime % 1 < .5f)
                Fill(new Rect(textArea.x + Width(result.Substring(start, cursor - start)), area.center.y - 9, 1, 18), _gold);
            return result;
        }
        private void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
