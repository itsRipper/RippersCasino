using System;
using UnityEngine;

namespace RippersCasino.Client
{
    internal sealed class RevolverArtwork : IDisposable
    {
        internal Texture2D Frame { get; }
        internal Texture2D Cylinder { get; }
        internal Texture2D Hammer { get; }
        internal Texture2D Trigger { get; }
        internal static readonly Rect CylinderBounds = new Rect(610, 74, 342, 310);
        internal static readonly Rect HammerBounds = new Rect(386, 29, 178, 116);
        internal static readonly Rect TriggerBounds = new Rect(609, 421, 108, 188);
        internal static readonly Vector2 HammerPivot = new Vector2(526, 123);
        internal static readonly Vector2 TriggerPivot = new Vector2(663, 432);
        internal const int Frames = 36, Columns = 6;
        private readonly Color32[] _pixels;
        private readonly int _width, _height;
        private static readonly Vector2[] HammerOutline =
        {
            new Vector2(392, 43), new Vector2(413, 34), new Vector2(443, 40), new Vector2(480, 66),
            new Vector2(510, 82), new Vector2(528, 72), new Vector2(539, 51), new Vector2(563, 61),
            new Vector2(550, 93), new Vector2(526, 118), new Vector2(474, 132), new Vector2(459, 111),
            new Vector2(432, 86), new Vector2(396, 64)
        };
        private static readonly Vector2[] TriggerOutline =
        {
            new Vector2(639, 428), new Vector2(681, 429), new Vector2(686, 437), new Vector2(666, 472),
            new Vector2(656, 509), new Vector2(665, 551), new Vector2(694, 582), new Vector2(709, 592),
            new Vector2(700, 601), new Vector2(682, 597), new Vector2(650, 575), new Vector2(627, 539),
            new Vector2(616, 493), new Vector2(622, 455)
        };
        internal RevolverArtwork(Texture2D original)
        {
            _width = original.width;
            _height = original.height;
            _pixels = original.GetPixels32();
            Hammer = Cut(HammerBounds, HammerOutline);
            Trigger = Cut(TriggerBounds, TriggerOutline);
            Frame = Texture(_width, _height, _pixels);
            var width = (int)CylinderBounds.width;
            var height = (int)CylinderBounds.height;
            var atlasWidth = width * Columns;
            var atlas = new Color32[atlasWidth * height * (Frames / Columns)];
            for (var frame = 0; frame < Frames; frame++)
            {
                var angle = frame * Math.PI * 2 / Frames;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var px = CylinderBounds.x + x;
                        var py = CylinderBounds.y + y;
                        var u = (px + .22f * py - 703) / 267;
                        var v = (py - 84 - 53 * u) / 238;
                        var alpha = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1 - u) * 120, Mathf.Min(v, 1 - v) * 120));
                        var color = Sample(px, py);
                        if (alpha > 0)
                        {
                            var surface = Math.Asin(Math.Max(-1, Math.Min(1, v * 2 - 1)));
                            var rotated = (float)((Math.Sin(surface + angle) + 1) * .5);
                            var sy = 84 + 53 * u + 238 * rotated;
                            var sx = 703 + 267 * u - .22f * sy;
                            var moving = Sample(sx, sy);
                            // Retain the aperture's edge shadow while the actual metal/flutes turn inside it.
                            var edge = Mathf.Clamp01(Mathf.Min(v, 1 - v) * 12);
                            color = Color32.Lerp(color, moving, edge);
                        }
                        var faceX = (px - 909 + .20f * (py - 254)) / 23;
                        var faceY = (py - 254) / 120;
                        var radius = Mathf.Sqrt(faceX * faceX + faceY * faceY);
                        var faceAlpha = Mathf.Clamp01((1 - radius) * 25);
                        if (faceAlpha > 0)
                        {
                            var rx = (float)(faceX * Math.Cos(angle) - faceY * Math.Sin(angle));
                            var ry = (float)(faceX * Math.Sin(angle) + faceY * Math.Cos(angle));
                            color = Color32.Lerp(color, Sample(909 + 23 * rx - .20f * 120 * ry, 254 + 120 * ry), faceAlpha);
                            alpha = Mathf.Max(alpha, faceAlpha);
                        }
                        color.a = (byte)(color.a * alpha);
                        var ax = frame % Columns * width + x;
                        var ay = frame / Columns * height + height - 1 - y;
                        atlas[ay * atlasWidth + ax] = color;
                    }
            }
            Cylinder = Texture(atlasWidth, height * (Frames / Columns), atlas);
            _pixels = null;
        }
        private Color32 Sample(float x, float y)
        {
            var ix = Mathf.Clamp(Mathf.RoundToInt(x), 0, _width - 1);
            var iy = Mathf.Clamp(Mathf.RoundToInt(y), 0, _height - 1);
            return _pixels[(_height - 1 - iy) * _width + ix];
        }
        private Texture2D Cut(Rect bounds, Vector2[] outline)
        {
            var width = (int)bounds.width;
            var height = (int)bounds.height;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var point = new Vector2(bounds.x + x, bounds.y + y);
                    if (!Inside(point, outline)) continue;
                    var index = (_height - 1 - (int)point.y) * _width + (int)point.x;
                    pixels[(height - 1 - y) * width + x] = _pixels[index];
                    _pixels[index] = new Color32(0, 0, 0, 0);
                }
            return Texture(width, height, pixels);
        }
        private static bool Inside(Vector2 point, Vector2[] polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
        private static Texture2D Texture(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(Frame);
            UnityEngine.Object.Destroy(Cylinder);
            UnityEngine.Object.Destroy(Hammer);
            UnityEngine.Object.Destroy(Trigger);
        }
    }
}
