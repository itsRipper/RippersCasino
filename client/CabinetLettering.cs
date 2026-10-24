using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RippersCasino.Client
{
    // A single ordered canvas keeps game IMGUI clipping out of the casino artwork.
    internal sealed class CabinetLettering : IDisposable
    {
        private readonly GameObject _root;
        private readonly List<Display> _displays = new List<Display>();
        private int _used;
        internal int PooledCount => _displays.Count;
        private readonly CanvasGroup _opacity;
        internal float Opacity
        {
            set
            {
                if (_opacity != null)
                    _opacity.alpha = value;
            }
        }
        private readonly Font _font;
        internal Font Font => _font;
        private sealed class Display
        {
            internal RectTransform Root; internal RawImage Image; internal Text Text;
            internal string FullText, FittedText;
            internal float Width;
            internal int FontSize;
            internal FontStyle FontStyle;
        }
        public CabinetLettering()
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 32);
            _root = new GameObject("RippersCasino display", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            _opacity = _root.GetComponent<CanvasGroup>();
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            UnityEngine.Object.DontDestroyOnLoad(_root);
        }
        public void BeginFrame()
        {
            _used = 0;
        }
        public void EndFrame()
        {
            for (var i = _used; i < _displays.Count; i++)
                if (_displays[i].Root.gameObject.activeSelf) _displays[i].Root.gameObject.SetActive(false);
        }
        private Display Acquire()
        {
            if (_used == _displays.Count)
            {
                var node = new GameObject("Casino graphic", typeof(RectTransform));
                node.transform.SetParent(_root.transform, false);
                var art = new GameObject("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                art.transform.SetParent(node.transform, false);
                var caption = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                caption.transform.SetParent(node.transform, false);
                var image = art.GetComponent<RawImage>();
                image.raycastTarget = false;
                var text = caption.GetComponent<Text>();
                text.raycastTarget = false;
                text.font = _font;
                text.supportRichText = false;
                foreach (var rect in new[] { image.rectTransform, text.rectTransform })
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
                _displays.Add(new Display { Root = node.GetComponent<RectTransform>(), Image = image, Text = text });
            }
            var display = _displays[_used++];
            if (!display.Root.gameObject.activeSelf) display.Root.gameObject.SetActive(true);
            return display;
        }
        private static void Position(Display display, Rect area)
        {
            var rect = display.Root;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
        }
        public void Draw(Rect logicalArea, Rect screenArea, string value, GUIStyle style)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(value))
                return;
            var display = Acquire();
            display.Image.enabled = false;
            display.Text.enabled = true;
            var text = display.Text;
            text.alignment = style.alignment;
            text.fontStyle = style.fontStyle;
            text.fontSize = Math.Max(10, Mathf.RoundToInt((style.fontSize > 0 ? style.fontSize : 16) * screenArea.height / logicalArea.height));
            text.resizeTextForBestFit = style.wordWrap;
            text.resizeTextMinSize = Math.Max(9, text.fontSize / 2);
            text.resizeTextMaxSize = text.fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = style.normal.textColor * GUI.color * (GUI.enabled ? Color.white : new Color(1, 1, 1, .38f));
            Position(display, screenArea);
            text.text = style.wordWrap ? value : FitLine(display, value, screenArea.width);
        }
        private static string FitLine(Display display, string value, float width)
        {
            var text = display.Text;
            if (display.FullText == value && display.Width == width && display.FontSize == text.fontSize && display.FontStyle == text.fontStyle)
                return display.FittedText;
            display.FullText = value;
            display.Width = width;
            display.FontSize = text.fontSize;
            display.FontStyle = text.fontStyle;
            var settings = text.GetGenerationSettings(new Vector2(width, 1000));
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            var generator = text.cachedTextGeneratorForLayout;
            if (generator.GetPreferredWidth(value, settings) <= width)
                return display.FittedText = value;
            var low = 0;
            var high = value.Length;
            while (low < high)
            {
                var length = (low + high + 1) / 2;
                if (generator.GetPreferredWidth(value.Substring(0, length) + "…", settings) <= width)
                    low = length;
                else
                    high = length - 1;
            }
            if (low > 0 && char.IsHighSurrogate(value[low - 1])) low--;
            return display.FittedText = value.Substring(0, low).TrimEnd() + "…";
        }
        public void Paint(Rect area, Texture texture, Color color, Rect uv, float rotation = 0)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var display = Acquire();
            display.Text.enabled = false;
            display.Image.enabled = true;
            display.Image.texture = texture;
            display.Image.color = color;
            display.Image.uvRect = uv;
            Position(display, area);
            if (rotation != 0)
            {
                display.Root.pivot = new Vector2(.5f, .5f);
                display.Root.anchoredPosition = new Vector2(area.center.x, -area.center.y);
                display.Root.localRotation = Quaternion.Euler(0, 0, -rotation);
            }
        }
        public void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            if (_font != null)
                UnityEngine.Object.Destroy(_font);
        }
    }
}
