using System;
using UnityEngine;
namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        // Page IDs stay stable because cashier/recovery routing does not follow navigation order.
        private static class Ui
        {
            public const int Width = 1440, Height = 900, Sidebar = 248, PageX = 280, PageY = 168, PageWidth = 1112, PageHeight = 664;
            public const int ButtonHeight = 48, CompactHeight = 40, ControlGap = 8;
            public const int InputSize = 16, BodySize = 18, MetaSize = 16, SectionSize = 20, HeadingSize = 28, NumberSize = 32;
        }
        private static readonly int[] NavOrder = { 0, 1, 3, 6, 7, 8, 9, 2, 4 };
        private static readonly int[] NavY = { 184, 264, 308, 352, 396, 440, 484, 576, 640 };
        private static readonly string[] FactoryFiles = { "factory-far.png", "factory-machinery.png", "factory-floor.png" };
        private static readonly float[] Parallax = { .15f, .35f, .55f };
        private readonly Texture2D[] _factoryLayers = new Texture2D[3];
        private GUIStyle _section, _navLabel, _sectionLabel, _inputLabel, _navigationLabel;
        private void DrawNavigation()
        {
            Fill(new Rect(0, 144, Ui.Sidebar, 712), new Color(.043f, .048f, .048f));
            Fill(new Rect(Ui.Sidebar, 144, 1, 712), _line);
            Label(new Rect(32, 236, 184, 20), "GAMES", _sectionLabel);
            Label(new Rect(32, 540, 184, 24), "ACCOUNT", _sectionLabel);
            Fill(new Rect(24, 696, 200, 1), _line);
            for (var row = 0; row < NavOrder.Length; row++)
            {
                var page = NavOrder[row];
                var y = NavY[row];
                var area = new Rect(16, y, 216, Ui.CompactHeight);
                var selected = _page == page;
                if (selected)
                    Fill(area, new Color(.14f, .12f, .09f));
                else if (area.Contains(Event.current.mousePosition))
                    Fill(area, new Color(.07f, .08f, .095f));
                if (selected)
                    Fill(new Rect(16, y, 3, Ui.CompactHeight), _gold);
                if (GUI.Button(area, string.Empty, GUIStyle.none))
                    Navigate(page);
                Fill(new Rect(32, y + 18, 4, 4), selected ? _gold : _line);
                Label(new Rect(48, y, 168, Ui.CompactHeight), PageNames[page], selected ? _navigationLabel : _barLeft);
            }
            Label(new Rect(48, 808, 184, 32), _state == null ? "LOADING…" : _tableError != null ? "UNAVAILABLE" : "", _barLeft);
        }
        private int StakeSelector(Rect area, int selected)
        {
            if (_state == null)
                return selected;
            var count = _state.bets.Length;
            var width = (area.width - (count - 1) * Ui.ControlGap) / Math.Max(1, count);
            for (var i = 0; i < count; i++)
                if (Button(new Rect(area.x + i * (width + Ui.ControlGap), area.y, width, Ui.ButtonHeight), _state.bets[i].ToString(), selected: i == selected))
                {
                    selected = i;
                    Play(CasinoCue.Bet);
                }
            return selected;
        }
        private void IndustrialStrip(Rect area, float travel)
        {
            BeginGroup(area);
            Fill(new Rect(0, 0, area.width, area.height), new Color(.055f, .078f, .091f));
            for (var layer = 0; layer < _factoryLayers.Length; layer++)
            {
                var texture = _factoryLayers[layer];
                if (texture == null)
                    continue;
                var offset = Mathf.Floor(travel * Parallax[layer]) % texture.width;
                for (var x = -offset; x < area.width; x += texture.width)
                    DrawTexture(new Rect(x, 0, texture.width, texture.height), texture);
            }
            EndGroup();
        }
    }
}
