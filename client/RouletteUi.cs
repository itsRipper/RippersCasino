using System;
using System.Linq;
using UnityEngine;
namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private static readonly int[] WheelNumbers = { 0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26 };
        private Texture2D _rouletteWheel;
        private GUIStyle _wheelNumber, _wheelResult;
        private int _rouletteDenomination;
        private string _heardRoulette, _heardRouletteResult;
        private float _rouletteTickAt;
        private readonly RoulettePresentation _roulettePresentation = new RoulettePresentation();
        private float RouletteProgress => (float)_roulettePresentation.Progress(Time.unscaledTime);
        private static bool RedNumber(int n) => n is 1 or 3 or 5 or 7 or 9 or 12 or 14 or 16 or 18 or 19 or 21 or 23 or 25 or 27 or 30 or 32 or 34 or 36;
        private Color RouletteColor(int n) => n == 0 ? new Color(.07f, .32f, .22f) : RedNumber(n) ? new Color(.46f, .09f, .10f) : new Color(.045f, .055f, .065f);
        private void ObserveRoulette(RouletteView old, RouletteView next, long serverTime)
        {
            if (next?.phase == "complete") _roulettePresentation.Observe(next.id, next.closedAt, serverTime, Time.unscaledTime);
            if (next?.phase == "complete" && _heardRoulette != next.id)
            {
                _heardRoulette = next.id;
                if (serverTime - next.closedAt < 4)
                    Play(CasinoCue.WheelSpin);
            }
        }
        private void RouletteWheel(Rect area, int target, float progress)
        {
            if (_rouletteWheel == null)
            {
                _rouletteWheel = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                var pixels = new Color[512 * 512];
                for (var y = 0; y < 512; y++)
                    for (var x = 0; x < 512; x++)
                    {
                        var dx = x - 255.5f;
                        var dy = 255.5f - y;
                        var radius = Mathf.Sqrt(dx * dx + dy * dy);
                        var angle = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90 + 360 + 180f / 37) % 360;
                        var pocket = (int)(angle / 360 * 37);
                        var light = .78f + .20f * Mathf.Cos((x + y) * .008f);
                        var grain = .018f * Mathf.Sin(radius * 1.3f + Mathf.Sin(angle * .07f));
                        var color = radius > 251 ? Color.clear : radius > 242 ? new Color(.27f, .20f, .12f) : radius > 238 ? new Color(.67f, .55f, .34f) : radius > 182 ? RouletteColor(WheelNumbers[pocket]) : radius > 173 ? new Color(.66f, .54f, .33f) : radius > 160 ? new Color(.16f, .20f, .19f) : radius > 148 ? new Color(.50f, .41f, .27f) : radius > 58 ? new Color(.20f + grain, .15f + grain, .11f + grain) : radius > 47 ? new Color(.60f, .49f, .31f) : new Color(.15f, .19f, .18f);
                        if (radius > 182 && radius < 238 && angle % (360f / 37) < .45f)
                            color = _gold;
                        if (radius > 160 && radius < 168 && angle % (360f / 37) < 1.1f)
                            color = new Color(.72f, .64f, .46f);
                        color *= new Color(light, light, light, 1);
                        color.a *= Mathf.Clamp01(252 - radius);
                        pixels[y * 512 + x] = color;
                    }
                _rouletteWheel.SetPixels(pixels);
                _rouletteWheel.Apply(false, true);
                _wheelResult = new GUIStyle(_heading) { alignment = TextAnchor.MiddleCenter };
                _wheelNumber = new GUIStyle(_smallCenter) { fontSize = 11, wordWrap = false };
            }
            var moving = target >= 0 && progress < 1;
            var rotation = moving ? (float)RoulettePresentation.WheelAngle(progress) : 0;
            DrawTexture(area, _rouletteWheel, rotation: rotation);
            for (var i = 0; area.width >= 250 && i < 37; i++)
            {
                var a = (i * 360f / 37 - 90 + rotation) * Mathf.Deg2Rad;
                var center = area.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * area.width * .413f;
                Label(new Rect(center.x - 12, center.y - 10, 24, 20), WheelNumbers[i].ToString(), _wheelNumber);
            }
            if (target >= 0)
            {
                var index = Array.IndexOf(WheelNumbers, target);
                var a = (float)RoulettePresentation.BallAngle(progress, index) * Mathf.Deg2Rad;
                var bounce = progress > .7f ? Mathf.Sin((progress - .7f) * 70) * (1 - progress) * .027f : 0;
                var center = area.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * area.width * (.375f + .080f * (1 - progress) + bounce);
                var old = GUI.color;
                GUI.color *= Color.white;
                Circle(center + new Vector2(2, 3), 5, new Color(0, 0, 0, .5f));
                Circle(center, 4.5f, new Color(.92f, .89f, .77f));
                Circle(center + new Vector2(-1, -1), 1.5f, Color.white);
                GUI.color = old;
                if (moving && Time.unscaledTime >= _rouletteTickAt)
                {
                    Play(CasinoCue.ReelStop);
                    _rouletteTickAt = Time.unscaledTime + .10f + progress * .22f;
                }
            }
            Label(new Rect(area.center.x - 70, area.center.y - 34, 140, 68), RoulettePresentation.Caption(target, progress, area.width < 250), target >= 0 && progress >= 1 ? _wheelResult : _smallCenter);
        }
        private static readonly string[] OutsideSelections = { "low", "even", "red", "black", "odd", "high" };
        private static readonly string[] OutsideNames = { "1–18", "EVEN", "RED", "BLACK", "ODD", "19–36" };
        private long RouletteAmount(string selection, int position)
        {
            var total = 0L;
            var bets = _tables?.table?.roulette?.bets;
            if (bets != null)
                foreach (var bet in bets)
                    if (bet.position == position && bet.selection == selection) total += bet.stake;
            return total;
        }
        private void RouletteField(Rect area, string name, string selection, long placed, int number = -1)
        {
            var hover = GUI.enabled && area.Contains(Event.current.mousePosition);
            var color = number >= 0 ? RouletteColor(number) : selection == "red" ? RouletteColor(1) : selection == "black" ? RouletteColor(2) : new Color(.065f, .17f, .14f);
            Fill(area, color);
            if (hover) Fill(area, new Color(1, 1, 1, .10f));
            Outline(area, placed > 0 || hover ? _gold : new Color(.39f, .45f, .36f));
            var horizontal = placed > 0 && area.width >= 90;
            Label(horizontal ? new Rect(area.x + 4, area.y + 2, area.width - 40, area.height - 4) : new Rect(area.x, area.y + 2, area.width, placed > 0 ? area.height - 30 : area.height - 4), name, _smallCenter);
            if (GUI.Button(area, string.Empty, GUIStyle.none))
            {
                Play(CasinoCue.ChipPlace);
                BeginTableAction("bet", amount: _state.bets[_rouletteDenomination], selection: selection);
            }
            if (placed > 0)
            {
                Badge(horizontal ? new Rect(area.xMax - 34, area.center.y - 12, 30, 24) : new Rect(area.center.x - 20, area.yMax - 26, 40, 24), placed.ToString(), true);
            }
        }
        private void RouletteEntry()
        {
            Fill(new Rect(0, 104, 464, 456), _panel);
            Outline(new Rect(0, 104, 464, 456), _line);
            RouletteWheel(new Rect(24, 124, 416, 416), -1, 1);
            Fill(new Rect(488, 104, 622, 456), _panel);
            Label(new Rect(512, 128, 574, 36), "European Roulette", _section);
            Label(new Rect(512, 184, 574, 62), "Choose your table bankroll.\nOpen betting and place your chips.", _text);
            Label(new Rect(512, 264, 574, 104), "STRAIGHT NUMBER    35:1\nDOZENS / COLUMNS     2:1\nOUTSIDE BETS               1:1", _text);
            Label(new Rect(512, 392, 174, 48), "TABLE BANKROLL", _barLeft);
            GUI.enabled = TableControls && _state?.available == true && _tables?.enabled == true && _tables.table == null;
            _tableBuyIn = QuantityField(new Rect(704, 392, 382, 48), _tableBuyIn);
            GUI.enabled = GUI.enabled && int.TryParse(_tableBuyIn, out var amount) && amount > 0 && amount <= _shownBalance && amount <= _tables.maxBuyIn;
            if (Button(new Rect(512, 464, 574, 48), "PLAY ROULETTE", primary: true))
            {
                _tableGame = 3;
                BeginTableAction("solo");
            }
            GUI.enabled = true;
            if (_tables?.table != null && Button(new Rect(512, 464, 574, 48), "RETURN TO GAME")) Navigate(TablePage(_tables.table.game));
            Label(new Rect(24, 590, 1058, 48), "Unused chips return when you cash out. Zero loses outside bets.", _muted);
            DrawTableRecovery(true);
        }
        private void CasinoRoulette()
        {
            Label(new Rect(0, 0, 1110, 40), "European Roulette", _heading);
            Label(new Rect(0, 48, 1110, 36), "Single zero · Numbers pay 35:1 · Dozens/columns 2:1 · Outside bets 1:1", _muted);
            var table = _tables?.table;
            if (table?.game != "roulette")
            {
                RouletteEntry();
                return;
            }
            var r = table.roulette;
            var mine = table.seats.First(s => s.mine);
            var betting = r?.phase == "betting";
            var seconds = (double)_tables.serverTime + (Time.unscaledTime - _tablesReceivedAt);
            var progress = r?.phase == "complete" ? RouletteProgress : 1;
            if (r?.phase == "complete" && progress >= 1 && _heardRouletteResult != r.id)
            {
                _heardRouletteResult = r.id;
                Play(CasinoCue.BallDrop);
                if (r.profits.ContainsKey(mine.position) && r.profits[mine.position] > 0)
                    Play(CasinoCue.Win);
            }
            Fill(new Rect(0, 104, 356, 468), _panel);
            Outline(new Rect(0, 104, 356, 468), _line);
            Fill(new Rect(372, 104, 738, 468), new Color(.031f, .052f, .048f));
            Outline(new Rect(372, 104, 738, 468), _line);
            Fill(new Rect(0, 584, 1110, 78), _panel);
            RouletteWheel(new Rect(8, 122, 340, 340), r?.number ?? -1, progress);
            Label(new Rect(24, 484, 308, 30), r?.phase == "complete" && progress >= 1 ? "RESULT / " + r.number : betting ? "BETTING OPEN" : "SINGLE ZERO", _smallCenter);
            Label(new Rect(24, 524, 308, 28), "Payouts include your wager", _smallCenter);
            GUI.enabled = TableControls && betting && _state.bets[_rouletteDenomination] <= _tables.maxWager;
            RouletteField(new Rect(380, 128, 48, 192), "0", "number:0", RouletteAmount("number:0", mine.position), 0);
            for (var col = 0; col < 12; col++)
                for (var row = 0; row < 3; row++)
                {
                    var number = col * 3 + 3 - row;
                    var key = "number:" + number;
                    RouletteField(new Rect(432 + col * 48, 128 + row * 64, 46, 62), number.ToString(), key, RouletteAmount(key, mine.position), number);
                }
            for (var row = 0; row < 3; row++)
            {
                var key = "column:" + (3 - row);
                RouletteField(new Rect(1010, 128 + row * 64, 98, 62), "2 TO 1", key, RouletteAmount(key, mine.position));
            }
            for (var i = 0; i < 3; i++)
            {
                var key = "dozen:" + (i + 1);
                RouletteField(new Rect(432 + i * 192, 328, 190, 48), (i + 1) + (i == 0 ? "ST" : i == 1 ? "ND" : "RD") + " 12", key, RouletteAmount(key, mine.position));
            }
            for (var i = 0; i < 6; i++)
                RouletteField(new Rect(432 + i * 96, 384, 94, 48), OutsideNames[i], OutsideSelections[i], RouletteAmount(OutsideSelections[i], mine.position));
            GUI.enabled = true;
            var committed = r?.bets.Where(b => b.position == mine.position).Sum(b => b.stake) ?? 0;
            for (var i = 0; i < 3; i++)
            {
                var x = 380 + i * 244;
                Fill(new Rect(x, 452, 232, 48), _panel);
                Label(new Rect(x + 12, 454, 208, 20), i == 0 ? "TABLE BANKROLL" : i == 1 ? "WAGERED" : "NET RESULT", _barLeft);
                Label(new Rect(x + 12, 475, 208, 24), i == 0 ? progress < 1 ? "—" : mine.stack.ToString("N0") : i == 1 ? committed.ToString("N0") : r?.phase == "complete" && progress >= 1 ? (r.profits.ContainsKey(mine.position) ? r.profits[mine.position] : 0).ToString("+0;-0;0") : "—", _navLabel);
            }
            Label(new Rect(392, 512, 698, 42), betting ? "Choose a chip, then click the board to place it.\nZero loses outside bets; no special refunds apply." : r?.phase == "complete" && progress < 1 ? "Bets closed. The wheel is turning…" : "Open betting for the next spin.", _muted);
            TableStatus(betting ? "Betting closes in " + Math.Max(0, (long)(r.deadline - seconds)) + "s" : r?.phase == "complete" && progress < 1 ? "Bets closed. Ball in play." : "Ready for the next spin.", betting ? "Place your chips before time runs out" : "Payouts include your wager");
            if (betting)
            {
                GUI.enabled = TableControls;
                _rouletteDenomination = StakeSelector(new Rect(0, 608, 390, 44), _rouletteDenomination);
                if (Button(new Rect(406, 608, 152, 44), "CLEAR BETS"))
                    BeginTableAction("bet", selection: "clear");
            }
            else
            {
                GUI.enabled = TableControls && progress >= 1;
                if (Button(new Rect(0, 608, 340, 44), "OPEN BETTING", primary: true))
                    BeginTableAction("start");
            }
            TableFooter(table, betting);
        }
    }
}
