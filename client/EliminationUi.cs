using System;
using System.Linq;
using UnityEngine;
namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private int _eliminationSequence;
        private readonly RoundWagerSelection _eliminationWager = new RoundWagerSelection();
        private string _eliminationRound, _heardEliminationWinner;
        private float _eliminationAt, _eliminationWinAt = -1;
        private Texture2D _muzzleFlash;
        private RevolverArtwork _revolverArtwork;
        private string _eliminationEffect;
        private EliminationView _eliminationBefore;
        private readonly RevolverTrigger _revolverTrigger = new RevolverTrigger();
        private void ObserveElimination(EliminationView old, EliminationView next)
        {
            if (next == null)
                return;
            if (_eliminationRound != next.id)
            {
                _eliminationRound = next.id;
                _eliminationEffect = null;
                _eliminationBefore = null;
                _eliminationWinAt = -1;
                _eliminationSequence = next.events.Length;
                _revolverTrigger.Seed(next.id, next.events.Length);
                _eliminationAt = Time.unscaledTime;
                Play(CasinoCue.Cylinder);
            }
            if (old?.id == next.id && next.events.Length > _eliminationSequence)
            {
                var last = next.events.Last();
                _eliminationSequence = next.events.Length;
                _eliminationAt = Time.unscaledTime;
                _eliminationEffect = last.result;
                _eliminationBefore = old;
                _revolverTrigger.Schedule(next.id, last.sequence, last.result, Time.unscaledTime);
                if (last.result == "safe" || last.result == "eliminated") Play(CasinoCue.Cylinder);
            }
            if (next.phase == "complete" && _heardEliminationWinner != next.id && !_revolverTrigger.Pending)
            {
                _heardEliminationWinner = next.id;
                var mine = _tables?.table?.seats?.FirstOrDefault(s => s.mine);
                if (_open && _page == 8 && mine?.position == next.winner)
                    Play(CasinoCue.Win);
            }
        }
        private void Circle(Vector2 center, float radius, Color color)
        {
            if (_feltMask == null)
            {
                _feltMask = new Texture2D(256, 128, TextureFormat.RGBA32, false);
                var pixels = new Color[256 * 128];
                for (var y = 0; y < 128; y++)
                    for (var x = 0; x < 256; x++)
                    {
                        var maskRadius = Mathf.Sqrt(Mathf.Pow((x - 127.5f) / 127f, 2) + Mathf.Pow((y - 63.5f) / 63f, 2));
                        pixels[y * 256 + x] = new Color(1, 1, 1, Mathf.Clamp01((1 - maskRadius) * 90));
                    }
                _feltMask.SetPixels(pixels);
                _feltMask.Apply(false, true);
            }
            var old = GUI.color;
            GUI.color *= color;
            DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _feltMask);
            GUI.color = old;
        }
        private void VirtualRevolver(Rect area, float recoil, float elapsed = -1)
        {
            if (_revolverArtwork == null) return;
            var height = area.width / 2;
            if (height > area.height) { area.x += (area.width - area.height * 2) / 2; area.width = area.height * 2; }
            else { area.y += (area.height - height) / 2; area.height = height; }
            area.x -= recoil * 16;
            area.y -= recoil * 6;
            var rotation = -recoil * 8;
            DrawTexture(area, _revolverArtwork.Frame, rotation: rotation);
            var trigger = _eliminationEffect == "safe" || _eliminationEffect == "eliminated";
            var phase = RevolverMotion.Cylinder(elapsed, _eliminationSequence, trigger) / 10;
            var frame = Mathf.FloorToInt(phase) % RevolverArtwork.Frames;
            RevolverCylinder(area, rotation, frame);
            var tint = GUI.color;
            GUI.color *= new Color(1, 1, 1, phase - Mathf.Floor(phase));
            RevolverCylinder(area, rotation, (frame + 1) % RevolverArtwork.Frames);
            GUI.color = tint;
            RevolverPart(area, rotation, RevolverArtwork.HammerBounds, _revolverArtwork.Hammer, RevolverArtwork.HammerPivot, RevolverMotion.Hammer(elapsed, trigger));
            RevolverPart(area, rotation, RevolverArtwork.TriggerBounds, _revolverArtwork.Trigger, RevolverArtwork.TriggerPivot, RevolverMotion.Trigger(elapsed, trigger));
        }
        private void RevolverCylinder(Rect weapon, float rotation, int frame)
        {
            var size = 1f / RevolverArtwork.Columns;
            var source = new Rect(frame % RevolverArtwork.Columns * size, frame / RevolverArtwork.Columns * size, size, size);
            RevolverPart(weapon, rotation, RevolverArtwork.CylinderBounds, _revolverArtwork.Cylinder, Vector2.zero, 0, source);
        }
        private void RevolverPart(Rect weapon, float rotation, Rect bounds, Texture texture, Vector2 pivot, float angle, Rect? source = null)
        {
            var center = bounds.center;
            var radians = angle * Mathf.Deg2Rad;
            var delta = center - pivot;
            center = pivot + new Vector2(delta.x * Mathf.Cos(radians) - delta.y * Mathf.Sin(radians), delta.x * Mathf.Sin(radians) + delta.y * Mathf.Cos(radians));
            var scale = weapon.width / 1776;
            var relative = center * scale - weapon.size / 2;
            radians = rotation * Mathf.Deg2Rad;
            center = weapon.center + new Vector2(relative.x * Mathf.Cos(radians) - relative.y * Mathf.Sin(radians), relative.x * Mathf.Sin(radians) + relative.y * Mathf.Cos(radians));
            var size = bounds.size * scale;
            DrawTexture(new Rect(center - size / 2, size), texture, source: source, rotation: rotation + angle);
        }
        private void RevolverFlash(Rect weapon, float shotTime, float recoil)
        {
            if (shotTime < 0 || shotTime >= .18f) return;
            if (_muzzleFlash == null)
            {
                _muzzleFlash = new Texture2D(256, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[256 * 128];
                for (var y = 0; y < 128; y++)
                    for (var x = 0; x < 256; x++)
                    {
                        var dx = x / 255f;
                        var dy = (y - 63.5f) / 64;
                        var edge = Mathf.Max(.001f, Mathf.Pow(1 - dx, 2) * (.18f + .30f * Mathf.Pow(Mathf.Sin(dx * 24), 6)));
                        var alpha = Mathf.Clamp01((edge - Mathf.Abs(dy)) * 20) * Mathf.Clamp01((1 - dx) * 3);
                        pixels[y * 256 + x] = Color.Lerp(new Color(1, .35f, .04f, alpha), new Color(1, .96f, .76f, alpha), Mathf.Clamp01((.55f - dx) * 3));
                    }
                _muzzleFlash.SetPixels(pixels);
                _muzzleFlash.Apply(false, true);
            }
            var angle = -recoil * 8 * Mathf.Deg2Rad;
            var offset = new Vector2(weapon.width * .48f, -weapon.height * .14f);
            var muzzle = weapon.center + new Vector2(Mathf.Cos(angle) * offset.x - Mathf.Sin(angle) * offset.y, Mathf.Sin(angle) * offset.x + Mathf.Cos(angle) * offset.y) - new Vector2(recoil * 16, recoil * 6);
            var color = GUI.color;
            GUI.color *= new Color(1, 1, 1, Mathf.Pow(1 - shotTime / .18f, 2));
            DrawTexture(new Rect(muzzle.x - 5, muzzle.y - 37, 125, 74), _muzzleFlash, rotation: -recoil * 8);
            GUI.color = color;
        }
        private void CasinoElimination()
        {
            Label(new Rect(0, 0, 1110, 40), "Russian Roulette", _heading);
            Label(new Rect(0, 48, 1110, 36), "Six chambers. One winner.", _muted);
            var table = _tables?.table;
            if (table?.game != "russian-roulette")
            {
                TableIntro(2, "Face a rival.\nChoose the chips you bring to the table.", 1, () => VirtualRevolver(new Rect(148, 150, 450, 250), 0));
                return;
            }
            var r = _eliminationBefore != null && Time.unscaledTime - _eliminationAt < (float)RevolverMotion.TriggerAt ? _eliminationBefore : table.elimination;
            var mine = table.seats.First(s => s.mine);
            var playing = r?.phase == "playing";
            _eliminationWager.Refresh(mine.stack);
            var wager = playing ? r.stake : _eliminationWager.Value;
            var pool = playing ? r.pool : _eliminationWager.Valid ? checked(2L * wager) : 0;
            Felt(new Rect(0, 104, 1110, 468));
            var elapsed = Time.unscaledTime - _eliminationAt;
            var shotTime = elapsed - (float)RevolverMotion.TriggerAt;
            var fired = _eliminationEffect == "eliminated";
            var recoil = fired && shotTime >= 0 ? Mathf.Sin(Mathf.Min(1, shotTime / .11f) * Mathf.PI * .5f) * Mathf.Exp(-shotTime * 11) : 0;
            var weapon = new Rect(306, 112, 500, 250);
            VirtualRevolver(weapon, recoil, _eliminationRound != null && (_eliminationEffect == null || _eliminationEffect == "safe" || fired) ? elapsed : -1);
            if (fired) RevolverFlash(weapon, shotTime, recoil);
            Label(new Rect(24, 124, 270, 24), "TABLE BANKROLL", _barLeft);
            Label(new Rect(24, 152, 270, 36), ChipCount(mine.stack), _section);
            Label(new Rect(24, 204, 270, 24), "WAGER PER MATCH", _barLeft);
            Label(new Rect(24, 232, 270, 36), wager > 0 ? ChipCount(wager) : "—", _section);
            Label(new Rect(24, 282, 270, 24), "PRIZE POOL", _barLeft);
            Label(new Rect(24, 310, 270, 36), pool > 0 ? ChipCount(pool) : "—", _section);
            if (r == null)
            {
                Label(new Rect(810, 128, 276, 100), "Your rival matches your wager.\nWinner takes the pool.", _text);
                for (var i = 0; i < table.seats.Length; i++)
                {
                    var seat = table.seats[i];
                    var x = i == 0 ? 112 : 656;
                    SeatPanel(new Rect(x, 370, 344, 120), seat.position, seat.name, seat.mine, false, false);
                    Label(new Rect(x + 86, 416, 240, 42), "Bankroll " + CashNumber(seat.stack), _text);
                }
                SeatPanel(new Rect(656, 370, 344, 120), 1 - mine.position, "RIVAL", false, false, false);
                Label(new Rect(742, 416, 240, 42), "Matches " + (wager > 0 ? ChipCount(wager) : "your wager"), _text);
            }
            else
            {
                var last = r.events.LastOrDefault();
                Label(new Rect(810, 124, 276, 24), playing ? "LAST TURN" : "LAST MATCH", _barLeft);
                Label(new Rect(810, 156, 276, 80), last == null ? "Your rival is ready." : r.players.First(p => p.position == last.position).name + "\n" + (last.result == "safe" ? "Empty chamber" : last.result == "eliminated" ? "Out" : "Forfeited"), _text);
                Label(new Rect(810, 254, 276, 80), playing ? "Empty chambers: " + r.emptyChambers + "\nMissing your turn forfeits the wager." : r.players.First(p => p.position == r.winner).name + " wins " + ChipCount(r.pool) + ".", _muted);
                for (var i = 0; i < r.players.Length; i++)
                {
                    var p = r.players[i];
                    var x = i == 0 ? 112 : 656;
                    var y = 370;
                    var active = playing && r.turn == p.position;
                    var outOfGame = p.status == "out" || p.status == "forfeit";
                    Fill(new Rect(x, y, 344, 94), active ? new Color(.23f, .20f, .12f) : new Color(.025f, .045f, .048f));
                    if (active || p.status == "winner")
                        Fill(new Rect(x, y, 344, 2), _gold);
                    var tint = GUI.color;
                    if (outOfGame)
                        GUI.color *= new Color(.55f, .55f, .55f, .7f);
                    Portrait(new Rect(x + 12, y + 12, 64, 70), p.position);
                    GUI.color = tint;
                    Label(new Rect(x + 86, y + 8, 246, 26), (p.position == mine.position ? "YOU / " : "") + p.name, _navLabel);
                    Label(new Rect(x + 86, y + 38, 246, 24), active ? p.position == mine.position ? "YOUR TURN" : "RIVAL'S TURN" : p.status == "in" ? "WAITING" : p.status == "winner" ? "WINNER" : p.status == "forfeit" ? "FORFEITED" : "OUT", _barLeft);
                    Label(new Rect(x + 86, y + 64, 246, 24), playing ? "Wager " + CashNumber(r.stake) : p.position == mine.position ? "Bankroll " + CashNumber(mine.stack) + " / Net " + (p.profit > 0 ? "+" : "") + CashNumber(p.profit) : "Net " + (p.profit > 0 ? "+" : "") + CashNumber(p.profit), _barLeft);
                }
                if (fired && shotTime >= 0 && shotTime < .24f)
                    Fill(new Rect(0, 104, 1110, 468), new Color(.6f, .06f, .05f, .26f * (1 - shotTime / .24f)));
            }
            TableStatus(playing ? "Turn: " + r.players.First(p => p.position == r.turn).name + " · " + Math.Max(0, r.deadline - (_tables.serverTime + (long)(Time.unscaledTime - _tablesReceivedAt))) + "s" : "Choose your wager.", playing ? "Timeout forfeits your wager" : _eliminationWager.Maximum > 0 ? "Up to " + CashNumber(_eliminationWager.Maximum) + " table chips / No fee" : "Cash out to change your bankroll");
            if (playing)
            {
                GUI.enabled = TableControls && elapsed >= (float)RevolverMotion.TriggerAt && r.turn == mine.position && r.players.First(p => p.position == mine.position).status == "in";
                if (Button(new Rect(0, 608, 390, 44), elapsed < (float)RevolverMotion.TriggerAt ? "CHAMBER TURNING…" : "TAKE TURN", primary: true))
                    BeginTableAction("take-turn");
            }
            else
            {
                GUI.enabled = TableControls && _eliminationWager.Maximum > 0 && (_eliminationWager.Value > 1 || !_eliminationWager.Valid);
                if (Button(new Rect(0, 608, 44, 44), "-")) _eliminationWager.Step(-1);
                GUI.enabled = TableControls && _eliminationWager.Maximum > 0;
                _eliminationWager.Text = QuantityField(new Rect(54, 608, 154, 44), _eliminationWager.Text, 10);
                GUI.enabled = TableControls && _eliminationWager.Maximum > 0 && (!_eliminationWager.Valid || _eliminationWager.Value < _eliminationWager.Maximum);
                if (Button(new Rect(218, 608, 44, 44), "+")) _eliminationWager.Step(1);
                GUI.enabled = TableControls && _eliminationWager.Valid;
                if (Button(new Rect(278, 608, 156, 44), "START MATCH", primary: true)) BeginTableAction("start", amount: _eliminationWager.Value);
                GUI.enabled = true;
                if (_tablePending == null) Label(new Rect(452, 608, 264, 44), _eliminationWager.Valid ? "Prize " + ChipCount(2L * _eliminationWager.Value) : _eliminationWager.Maximum > 0 ? "Enter 1–" + CashNumber(_eliminationWager.Maximum) + " chips" : "No table chips. Cash out.", _smallCenter);
            }
            TableFooter(table, playing);
        }
    }
}
