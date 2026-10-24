using System;
using System.Collections.Generic;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private Texture2D _chipIcon, _backroomTable, _revolverArt, _slotCabinet, _cardAtlas, _tableArt, _portraitAtlas;
        private readonly Dictionary<string, int> _cardFaces = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _cardFlips = new Dictionary<string, float>();
        private bool TableRevealPending
        {
            get
            {
                var table = _tables?.table;
                if (table?.roulette?.phase == "complete")
                    return RouletteProgress < 1;
                return table?.elimination != null && _eliminationBefore != null && Time.unscaledTime - _eliminationAt < .65f;
            }
        }
        private void Outline(Rect area, Color color)
        {
            Fill(new Rect(area.x, area.y, area.width, 1), color);
            Fill(new Rect(area.x, area.yMax - 1, area.width, 1), color);
            Fill(new Rect(area.x, area.y, 1, area.height), color);
            Fill(new Rect(area.xMax - 1, area.y, 1, area.height), color);
        }
        private void Portrait(Rect area, int position)
        {
            DrawTexture(area, _portraitAtlas, source: new Rect((position % 6) / 6f, 0, 1f / 6, 1));
        }
        private void Chips(Vector2 point, long value, int height = 0)
        {
            if (value <= 0) return;
            var count = height > 0 ? height : Math.Min(5, 1 + (int)Math.Log10(Math.Max(1, value)));
            for (var i = 0; i < count; i++)
                DrawTexture(new Rect(point.x - 14, point.y - 17 - i * 3, 28, 28), _chipIcon, ScaleMode.ScaleToFit);
        }
        private void SeatPanel(Rect area, int position, string name, bool mine, bool active, bool winner)
        {
            Fill(new Rect(area.x + 2, area.y + 4, area.width, area.height), new Color(0, 0, 0, .25f));
            Fill(area, new Color(.035f, .052f, .051f, .97f));
            Outline(area, active ? _gold : winner ? new Color(.34f, .50f, .36f) : _line);
            if (active || winner)
                Fill(new Rect(area.x + 1, area.y + 1, area.width - 2, 3), active ? _gold : new Color(.34f, .50f, .36f));
            Portrait(new Rect(area.x + 10, area.y + 13, 52, 64), position);
            Label(new Rect(area.x + 74, area.y + 6, area.width - 86, 24), (mine ? "YOU / " : "") + name, _navLabel);
        }
        private void TableStatus(string headline, string detail)
        {
            Fill(new Rect(0, 568, 1110, 36), _panel);
            Fill(new Rect(0, 568, 1110, 1), _line);
            Label(new Rect(12, 572, 660, 28), headline, _navLabel);
            Label(new Rect(688, 572, 410, 28), detail, _barRight);
        }
        private void Badge(Rect area, string title, bool brass = false)
        {
            Fill(area, brass ? new Color(.35f, .29f, .19f) : new Color(.075f, .10f, .10f));
            Outline(area, brass ? _gold * .7f : _line * 1.8f);
            Label(area, title, _smallCenter);
        }
    }
}
