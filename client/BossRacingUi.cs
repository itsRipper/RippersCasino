using System;
using System.Linq;
using UnityEngine;
namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private static readonly string[] BossIds = { "killa", "tagilla", "gluhar", "shturman", "sanitar", "reshala" };
        private static readonly string[] BossNames = { "Killa", "Tagilla", "Glukhar", "Shturman", "Sanitar", "Reshala" };
        private static string BossName(string id)
        {
            var index = Array.IndexOf(BossIds, id);
            return index < 0 ? id : BossNames[index];
        }
        private readonly Texture2D[] _runnerSheets = new Texture2D[6];
        private readonly RacePresentation _racePresentation = new RacePresentation();
        private CasinoReceipt _lastRace;
        private int _selectedRacer, _raceBet;
        private float _raceAt, _raceClockRate = 1, _raceFootAt;
        private double RaceElapsed => (Time.unscaledTime - _raceAt) * _raceClockRate;
        private static string ChipCount(long value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + (value == 1 ? " CHIP" : " CHIPS");
        private bool GameActive => _presentation.Active || _racePresentation.Active;
        private bool RacingReady
        {
            get
            {
                var racing = _state?.racing;
                if (racing?.enabled != true || racing.machine != "boss-racing" || racing.racers?.Length != 6)
                    return false;
                for (var i = 0; i < 6; i++)
                    if (racing.racers[i] == null || racing.racers[i].id != BossIds[i])
                        return false;
                return true;
            }
        }
        private void BeginRace(int amount)
        {
            _raceClockRate = _slowReels.Value ? .2f : 1;
            _racePresentation.Begin(_shownBalance, amount);
            _shownBalance = _racePresentation.VisibleBalance;
            _raceAt = Time.unscaledTime;
            Play(CasinoCue.RaceStart);
            _message = "Waiting for the start…";
        }
        private bool ValidRaceReceipt(CasinoReceipt receipt, CasinoRequest request)
        {
            if (!RacingReady || receipt.selection != request.selection || receipt.amount != request.amount || receipt.finishOrder?.Length != 6 || receipt.finishOrder.Distinct().Count() != 6 || receipt.finishOrder.Any(id => !BossIds.Contains(id)) || receipt.winner != receipt.finishOrder[0])
                return false;
            var racer = _state.racing.racers.FirstOrDefault(r => r.id == request.selection);
            return racer != null && receipt.payout == (receipt.winner == request.selection ? (long)request.amount * racer.returnMultiplier : 0);
        }
        private void ResolveRace(CasinoReceipt receipt)
        {
            if (!_racePresentation.Active)
                _racePresentation.Begin(checked(receipt.balance - receipt.payout + receipt.amount), checked((int)receipt.amount));
            var order = receipt.finishOrder.Select(id => Array.IndexOf(BossIds, id)).ToArray();
            _racePresentation.Resolve(order, receipt.balance, receipt.payout);
            _raceAt = Time.unscaledTime;
            _lastRace = receipt;
            _message = "Race in progress · Bets closed";
        }
        private void TickRace()
        {
            if (!_racePresentation.Active)
                return;
            if (!_racePresentation.Revealed && _racePresentation.Resolved && _page == 3 && Time.unscaledTime >= _raceFootAt)
            {
                _raceFootAt = Time.unscaledTime + .16f;
                Play(CasinoCue.RaceStep);
            }
            var reveal = _racePresentation.Tick(RaceElapsed);
            _shownBalance = _racePresentation.VisibleBalance;
            if (reveal)
            {
                _message = BossName(_lastRace.winner).ToUpperInvariant() + " WINS  /  " + (_lastRace.payout > 0 ? "PAID " + _lastRace.payout.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " CHIPS" : "NO WIN");
                Play(_lastRace.payout > 0 ? CasinoCue.Win : CasinoCue.RaceFinish);
                if (_lastRace.payout > 0)
                    Play(CasinoCue.Payout);
            }
        }
        private void DrawRunner(Rect area, int racer, int frame)
        {
            // Integer-sized physical pixels where the viewport permits it. Tiny
            // windows use point-filtered downscaling, always with a square aspect.
            var scale = GUI.matrix.MultiplyVector(Vector3.right).x;
            var pixels = Mathf.Floor(Math.Min(area.width, area.height) * scale);
            if (pixels >= 48)
                pixels = Mathf.Floor(pixels / 48) * 48;
            var x = Mathf.Round((area.x + _guiOrigin.x) * scale + (area.width * scale - pixels) / 2);
            var y = Mathf.Round((area.y + _guiOrigin.y) * scale + (area.height * scale - pixels) / 2);
            area = new Rect(x / scale - _guiOrigin.x, y / scale - _guiOrigin.y, pixels / scale, pixels / scale);
            // Atlas rows are authored top-to-bottom; Unity UVs start at the bottom.
            var uv = new Rect((frame % 4) * .25f, frame < 4 ? .5f : 0, .25f, .5f);
            DrawTexture(area, _runnerSheets[racer], ScaleMode.StretchToFill, uv);
        }
        private void CasinoRacing()
        {
            Label(new Rect(0, 0, 1080, 40), "Boss Racing", _heading);
            Label(new Rect(1, 48, 1080, 30), "Six bosses. One finish line. Choose your runner.", _muted);
            if (!RacingReady)
            {
                Fill(new Rect(0, 98, 1110, 160), _panel);
                Label(new Rect(24, 120, 1060, 100), "Boss Racing is unavailable.\nClose the casino and try again.", _text);
                return;
            }
            var racers = _state.racing.racers;
            for (var i = 0; i < 6; i++)
            {
                var x = i * 186;
                var selected = i == _selectedRacer;
                Fill(new Rect(x, 94, 180, 151), selected ? _gold : _line);
                Fill(new Rect(x + 2, 96, 176, 147), _panel);
                Label(new Rect(x + 9, 100, 24, 24), "0" + (i + 1), _sectionLabel);
                DrawRunner(new Rect(x + 42, 96, 96, 96), i, 0);
                Label(new Rect(x + 8, 184, 164, 24), BossNames[i], _center);
                Label(new Rect(x + 8, 212, 164, 24), racers[i].returnMultiplier + "× PAYOUT", _smallCenter);
                GUI.enabled = Available;
                if (GUI.Button(new Rect(x, 94, 180, 151), string.Empty, GUIStyle.none))
                {
                    _selectedRacer = i;
                    Play(CasinoCue.Bet);
                }
                GUI.enabled = true;
            }
            var running = _racePresentation.Active && !_racePresentation.Revealed;
            var showResult = _lastRace != null && !running;
            var elapsed = RaceElapsed;
            Fill(new Rect(0, 266, 1110, 282), _panel);
            IndustrialStrip(new Rect(140, 267, 829, 280), running ? (float)elapsed * 12 : 0);
            for (var i = 0; i < 6; i++)
            {
                var y = 277 + i * 44;
                var selected = i == _selectedRacer;
                if (selected)
                    Fill(new Rect(1, y - 3, 1108, 44), new Color(.44f, .34f, .17f, .09f));
                Label(new Rect(14, y + 7, 122, 28), BossNames[i], selected ? _text : _muted);
                Fill(new Rect(140, y + 37, 829, 3), new Color(.075f, .11f, .12f));
                for (var rail = 0; rail < 12; rail++)
                    Fill(new Rect(150 + rail * 68, y + 38, 28, 1), new Color(.29f, .31f, .26f));
                var progress = _racePresentation.Resolved ? _racePresentation.Position(i, elapsed) : showResult ? 1 : 0;
                if (selected) Fill(new Rect(140, y + 36, 829, 1), new Color(.52f, .43f, .24f, .45f));
                var moving = running && _racePresentation.Resolved && progress < 1;
                var frame = moving ? ((int)(elapsed * 16) + i) % 8 : 0;
                DrawRunner(new Rect(128 + (float)progress * 788, y - 6, 48, 48), i, frame);
                if (showResult)
                {
                    var rank = _lastRace.finishOrder == null ? -1 : Array.IndexOf(_lastRace.finishOrder, racers[i].id);
                    Label(new Rect(984, y + 7, 110, 27), rank < 0 ? "—" : rank == 0 ? "WINNER" : "#" + (rank + 1), rank == 0 ? _text : _muted);
                }
                else
                    Label(new Rect(984, y + 7, 110, 27), running ? "LIVE" : "READY", _small);
            }
            for (var n = 0; n < 27; n++)
            {
                Fill(new Rect(955, 277 + n * 10, 6, 10), n % 2 == 0 ? _gold : _line);
                Fill(new Rect(961, 277 + n * 10, 6, 10), n % 2 == 0 ? _line : _gold);
            }
            var chosen = racers[_selectedRacer];
            var stake = _state.bets[_raceBet];
            Label(new Rect(0, 552, 1100, 32), BossNames[_selectedRacer].ToUpperInvariant() + "  /  " + ChipCount(stake) + "  /  WIN RETURNS " + ChipCount((long)stake * chosen.returnMultiplier), _text);
            GUI.enabled = Available;
            _raceBet = StakeSelector(new Rect(0, 592, 440, Ui.ButtonHeight), _raceBet);
            GUI.enabled = true;
            Label(new Rect(464, 592, 300, Ui.ButtonHeight), "Choose a runner and wager.\nPayouts include your wager.", _small);
            GUI.enabled = Available && _shownBalance >= stake;
            if (Button(new Rect(792, 592, 318, Ui.ButtonHeight), _racePresentation.Active || _busy ? "RACE IN PROGRESS…" : "START RACE", primary: true))
                Begin("race", stake, chosen.id);
            GUI.enabled = true;
            var result = showResult ? "LAST RACE  /  " + BossName(_lastRace.winner).ToUpperInvariant() + " WON  /  YOUR PICK: " + BossName(_lastRace.selection).ToUpperInvariant() + "  /  PAID " + ChipCount(_lastRace.payout) : "";
            Label(new Rect(1, 641, 1110, 22), result, _small);
        }
    }
}
