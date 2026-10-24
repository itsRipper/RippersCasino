using System;
using System.Linq;
using UnityEngine;
namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private int _pokerBlind = 2;
        private readonly float[] _pokerBetAt = new float[6];
        private static readonly Vector2[] PokerPositions = { new Vector2(399, 418), new Vector2(36, 118), new Vector2(762, 118), new Vector2(36, 357), new Vector2(762, 357), new Vector2(399, 98) };
        private static Rect PokerSeat(int position) => new Rect(PokerPositions[position % 6], new Vector2(310, 134));
        private float _pokerSettledAt;
        private string _raiseTo = "4", _heardPoker;
        private void ObservePoker(PokerView old, PokerView next)
        {
            if (next == null)
                return;
            if (old?.id != next.id)
            {
                _cardArrivals.Clear();
                _cardFaces.Clear();
                _cardFlips.Clear();
            }
            if (old?.id != next.id || old.board.Length != next.board.Length)
                Play(CasinoCue.CardDeal);
            foreach (var player in next.players)
                if (player.position >= 0 && player.position < 6 && old?.players.FirstOrDefault(p => p.position == player.position)?.bet != player.bet)
                {
                    _pokerBetAt[player.position] = Time.unscaledTime;
                    if (player.bet > 0) Play(CasinoCue.ChipPlace);
                }
            if (next.phase == "complete" && _heardPoker != next.id)
            {
                _heardPoker = next.id;
                _pokerSettledAt = Time.unscaledTime;
                var mine = _tables?.table?.seats?.FirstOrDefault(s => s.mine);
                if (next.players.FirstOrDefault(p => p.position == mine?.position)?.profit > 0)
                    Play(CasinoCue.Win);
            }
        }
        private void TableIntro(int game, string text, int minimum = 2, Action preview = null)
        {
            Felt(new Rect(0, 104, 1110, 400));
            if (preview != null)
                preview();
            else
            {
                PlayingCard(new Rect(400, 210, 80, 112), 0, "intro-a");
                PlayingCard(new Rect(490, 210, 80, 112), 12, "intro-k");
            }
            Label(new Rect(610, 218, 380, 116), text, _text);
            Label(new Rect(24, 538, 180, 48), "TABLE BANKROLL", _barLeft);
            GUI.enabled = TableControls && _state?.available == true && _tables?.enabled == true && _tables.table == null;
            _tableBuyIn = QuantityField(new Rect(208, 538, 180, 48), _tableBuyIn);
            GUI.enabled = GUI.enabled && int.TryParse(_tableBuyIn, out var buyIn) && buyIn >= minimum && buyIn <= _shownBalance && buyIn <= (_tables?.maxBuyIn ?? 5000);
            if (Button(new Rect(416, 538, 300, 48), "TAKE A SEAT", primary: true))
            {
                _tableGame = game;
                BeginTableAction("solo");
            }
            GUI.enabled = true;
            if (_tables?.table != null && Button(new Rect(740, 538, 346, 48), "RETURN TO GAME"))
                Navigate(TablePage(_tables.table.game));
            Label(new Rect(24, 606, _tablePending == null ? 1062 : 690, 42), _tables?.table != null ? "Cash out of your current game before opening another." : "Bankroll " + CashNumber(minimum) + "–" + CashNumber(_tables?.maxBuyIn ?? 5000) + " chips. Unused chips return when you cash out.", _small);
            DrawTableRecovery(true);
        }
        private void TableFooter(TableView table, bool playing)
        {
            GUI.enabled = TableControls;
            if (Button(new Rect(738, 608, 180, 44), "CASINO HOME"))
                Navigate(0);
            GUI.enabled = TableControls && !TableRevealPending;
            if (Button(new Rect(930, 608, 180, 44), playing ? table.game == "russian-roulette" ? "FORFEIT / LEAVE" : "LEAVE AFTER HAND" : "CASH OUT"))
                BeginTableAction("leave");
            GUI.enabled = true;
            DrawTableRecovery();
        }
        private void CasinoPoker()
        {
            Label(new Rect(0, 0, 1110, 40), "Texas Hold’em", _heading);
            Label(new Rect(0, 48, 1110, 36), "No-limit Hold’em · No house fee · Timeout: check or fold", _muted);
            var table = _tables?.table;
            if (table?.game != "poker")
            {
                TableIntro(1, "Play against one to four rivals.\nChoose your table bankroll.");
                return;
            }
            var r = table.poker;
            var mine = table.seats.First(s => s.mine);
            var playing = r?.phase == "playing";
            Felt(new Rect(0, 104, 1110, 468));
            if (r == null)
            {
                Label(new Rect(380, 278, 350, 88), "Set the big blind, then deal.\nThe small blind is half the big blind.\nNo ante or house fee.", _center);
                for (var i = 0; i < table.seats.Length; i++)
                {
                    var seat = table.seats[i];
                    var area = PokerSeat(seat.position);
                    SeatPanel(area, seat.position, seat.name, seat.mine, false, false);
                    Label(new Rect(area.x + 74, area.y + 44, 220, 58), ChipCount(seat.stack) + "\n" + "BANKROLL", _text);
                }
            }
            else
            {
                Badge(new Rect(432, 236, 248, 27), r.street.ToUpperInvariant() + "  /  POT " + r.pot, true);
                Chips(new Vector2(706, 255), r.pot, 3);
                for (var i = 0; i < 5; i++)
                    PlayingCard(new Rect(399 + i * 64, 274, 56, 77), i < r.board.Length ? r.board[i] : -1, r.id + ":board:" + i, i);
                Fill(new Rect(380, 358, 350, 48), _panel);
                Label(new Rect(392, 362, 326, 40), !playing && r.players.Count(p => p.profit > 0) > 1 ? "Split pot · " + r.players.Count(p => p.profit > 0) + " winners" : r.events.LastOrDefault() ?? "", _smallCenter);
                for (var i = 0; i < r.players.Length; i++)
                {
                    var p = r.players[i];
                    var seatArea = PokerSeat(p.position);
                    var x = seatArea.x;
                    var y = seatArea.y;
                    var active = playing && r.turn == p.position;
                    SeatPanel(seatArea, p.position, p.name, p.position == mine.position, active, !playing && p.profit > 0);
                    if (r.button == p.position) Badge(new Rect(x + 8, y + 102, 28, 22), "D", true);
                    if (p.action == "SMALL BLIND" || p.action == "BIG BLIND")
                        Badge(new Rect(x + 40, y + 102, 38, 22), p.action == "SMALL BLIND" ? "SB" : "BB");
                    var old = GUI.color;
                    var foldOffset = 0f;
                    var foldScale = 1f;
                    if (p.folded)
                    {
                        var foldKey = r.id + ":fold:" + p.position;
                        if (!_cardArrivals.TryGetValue(foldKey, out var foldedAt))
                            _cardArrivals[foldKey] = foldedAt = Time.unscaledTime;
                        var fold = Mathf.SmoothStep(0, 1, (Time.unscaledTime - foldedAt) / .3f);
                        GUI.color *= new Color(1, 1, 1, Mathf.Lerp(1, .25f, fold));
                        foldOffset = fold * 12;
                        foldScale = Mathf.Lerp(1, .75f, fold);
                    }
                    for (var c = 0; c < 2; c++)
                        PlayingCard(new Rect(x + 74 + c * 50 + 24 * (1 - foldScale), y + 32 + foldOffset, 48 * foldScale, 66 * foldScale), p.cards[c], r.id + ":hole:" + p.position + ":" + c, c);
                    GUI.color = old;
                    Label(new Rect(x + 178, y + 32, 120, 28), ChipCount(p.stack), _valueLeft);
                    Label(new Rect(x + 178, y + 60, 88, 28), "BET " + p.bet, _valueLeft);
                    var motion = Mathf.SmoothStep(0, 1, (Time.unscaledTime - _pokerBetAt[p.position]) / .32f);
                    Chips(new Vector2(x + 286, y + 85), p.stack, 3);
                    if (motion < 1)
                        Chips(Vector2.Lerp(new Vector2(x + 260, y + 83), new Vector2(706, 255), motion), p.bet, 2);
                    var awarded = Mathf.SmoothStep(0, 1, (Time.unscaledTime - _pokerSettledAt) / .55f);
                    if (!playing && p.profit > 0 && awarded < 1)
                        Chips(Vector2.Lerp(new Vector2(706, 255), new Vector2(x + 286, y + 85), awarded), p.profit, 3);
                    Label(new Rect(x + 84, y + 104, 214, 24), playing ? p.action : p.result + " / " + (p.profit > 0 ? "+" : "") + p.profit, _valueLeft);
                }
            }
            TableStatus(playing ? "Turn: " + r.players.First(p => p.position == r.turn).name + " · " + Math.Max(0, r.deadline - (_tables.serverTime + (long)(Time.unscaledTime - _tablesReceivedAt))) + "s" : "Set the big blind, then deal.", playing ? "Raise to: your total bet this round" : "Small blind: half the big blind");
            if (playing)
            {
                GUI.enabled = TableControls && r.turn == mine.position;
                if (Button(new Rect(0, 608, 100, 44), "FOLD"))
                    BeginTableAction("fold");
                if (Button(new Rect(108, 608, 140, 44), r.call == 0 ? "CHECK" : "CALL " + r.call, primary: true))
                    BeginTableAction(r.call == 0 ? "check" : "call");
                _raiseTo = QuantityField(new Rect(256, 608, 104, 44), _raiseTo);
                GUI.enabled = GUI.enabled && r.canRaise && int.TryParse(_raiseTo, out var raise) && raise > 0 && raise <= r.maxRaise && (raise >= r.minRaise || raise == r.maxRaise);
                if (Button(new Rect(368, 608, 104, 44), "RAISE TO"))
                    BeginTableAction("raise", amount: int.Parse(_raiseTo));
                GUI.enabled = TableControls && r.turn == mine.position && (r.canRaise || r.maxRaise <= r.call + r.players.First(p => p.position == mine.position).bet);
                if (Button(new Rect(480, 608, 100, 44), "ALL-IN"))
                    BeginTableAction("all-in");
            }
            else
            {
                GUI.enabled = TableControls;
                var maximum = (int)Math.Min(_tables.maxWager, table.seats.Min(seat => seat.stack));
                maximum -= maximum % 2;
                _pokerBlind = maximum < 2 ? 0 : Math.Max(2, Math.Min(maximum, _pokerBlind));
                GUI.enabled = TableControls && _pokerBlind > 2;
                var betX = 0;
                if (Button(new Rect(betX, 608, 56, 44), "-"))
                    _pokerBlind = Math.Max(2, _pokerBlind - 2);
                Label(new Rect(betX + 56, 608, 112, 44), "BIG BLIND " + _pokerBlind, _smallCenter);
                GUI.enabled = TableControls && _pokerBlind < maximum;
                if (Button(new Rect(betX + 168, 608, 56, 44), "+"))
                    _pokerBlind = Math.Min(_tables.maxWager, _pokerBlind + 2);
                GUI.enabled = TableControls && _pokerBlind >= 2 && table.seats.All(s => s.stack >= _pokerBlind);
                if (Button(new Rect(betX + 236, 608, 170, 44), "DEAL", primary: true))
                    BeginTableAction("start", amount: _pokerBlind);
            }
            TableFooter(table, playing);
        }
    }
}
