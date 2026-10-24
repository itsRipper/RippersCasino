using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private readonly BlackjackBetSelection _blackjackBet = new BlackjackBetSelection();
        private static string BlackjackStatus(string status) => status == "win" ? "Won" : status == "lose" ? "Lost" : status == "stand" ? "Stood" : status == "push" ? "Push" : status == "bust" ? "Bust" : status == "blackjack" ? "Blackjack" : "Waiting";
        private void RefreshBlackjackBet()
        {
            var table = _tables?.table;
            var available = table?.game == "blackjack" && table.seats.Length > 0 ? table.seats.Min(seat => seat.stack) : 0;
            _blackjackBet.Refresh(available, _tables?.maxWager ?? 1000);
        }
        private static Vector2 BlackjackPosition(int index, int count)
        {
            if (count == 1) return new Vector2(384, 344);
            if (count == 2) return new Vector2(index == 0 ? 112 : 656, 330);
            return index == 0 ? new Vector2(384, 368) : new Vector2(index == 1 ? 24 : 744, 300);
        }
        private float _tablesReceivedAt;
        private string _heardBlackjack;
        private Texture2D _feltMask;
        private readonly Dictionary<string, float> _cardArrivals = new Dictionary<string, float>();
        private void ObserveBlackjack(BlackjackView previous, BlackjackView next)
        {
            if (next == null)
                return;
            if (previous?.id != next.id)
            {
                _cardArrivals.Clear();
                _cardFaces.Clear();
                _cardFlips.Clear();
                Play(CasinoCue.CardDeal);
            }
            else if (next.hands.Sum(hand => hand.cards.Length) > previous.hands.Sum(hand => hand.cards.Length) || next.phase != previous.phase)
                Play(CasinoCue.CardDeal);
            if (next.phase == "complete" && _heardBlackjack != next.id)
            {
                _heardBlackjack = next.id;
                var mine = _tables?.table?.seats?.FirstOrDefault(seat => seat.mine);
                if (next.hands.FirstOrDefault(hand => hand.position == mine?.position)?.profit > 0)
                    Play(CasinoCue.Win);
            }
        }
        private void Felt(Rect area)
        {
            DrawTexture(area, _page == 8 ? _backroomTable : _tableArt);
        }
        private void PlayingCard(Rect area, int card, string key, int order = 0)
        {
            if (!_cardArrivals.TryGetValue(key, out var arrived))
                _cardArrivals[key] = arrived = Time.unscaledTime + order * .055f;
            var progress = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - arrived) / .28f));
            if (_cardFaces.TryGetValue(key, out var oldCard) && oldCard < 0 && card >= 0)
            {
                _cardFlips[key] = Time.unscaledTime;
                Play(CasinoCue.CardFlip);
            }
            _cardFaces[key] = card;
            var flip = _cardFlips.TryGetValue(key, out var flippedAt) ? Mathf.Clamp01((Time.unscaledTime - flippedAt) / .28f) : 1;
            var face = flip < .5f || card < 0 || card > 51 ? 52 : card;
            var width = area.width;
            area.width *= Mathf.Max(.04f, Mathf.Abs(Mathf.Cos(flip * Mathf.PI)));
            area.x += (width - area.width) / 2;
            if ((_page == 6 || _page == 7) && !key.StartsWith("home") && !key.StartsWith("preview") && !key.StartsWith("intro"))
                area.position = Vector2.Lerp(new Vector2(540, 142), area.position, progress);
            else
                area.y -= (1 - progress) * 12;
            var old = GUI.color;
            GUI.color *= new Color(1, 1, 1, Mathf.Clamp01(progress * 3));
            Fill(new Rect(area.x + 2, area.y + 3, area.width, area.height), new Color(0, 0, 0, .28f));
            DrawTexture(area, _cardAtlas, source: new Rect((face % 10) * .1f, 1 - (face / 10 * 154 + 132) / 924f, .1f, 132 / 924f), rotation: (1 - progress) * -9);
            GUI.color = old;
        }
        private void CasinoBlackjack()
        {
            Label(new Rect(0, 0, 1110, 40), "Blackjack", _heading);
            Label(new Rect(0, 48, 1110, 36), (_tables?.blackjackDecks ?? 6) + " decks · Blackjack pays 3:2 · Dealer stands on soft 17", _muted);
            var table = _tables?.table;
            if (table?.game != "blackjack")
            {
                TableIntro(0, "Face the dealer alongside up to two rivals.\nChoose your table bankroll.");
                return;
            }
            var round = table.blackjack;
            Felt(new Rect(0, 104, 1110, 468));
            Badge(new Rect(462, 112, 188, 28), "DEALER / HOUSE");
            if (round == null)
            {
                PlayingCard(new Rect(475, 150, 64, 90), -1, "waiting-a");
                PlayingCard(new Rect(551, 150, 64, 90), -1, "waiting-b", 1);
                for (var i = 0; i < table.seats.Length; i++)
                {
                    var seat = table.seats[i];
                    var point = BlackjackPosition(i, table.seats.Length);
                    SeatPanel(new Rect(point.x, point.y, 344, 184), seat.position, seat.name, seat.mine, false, false);
                    Label(new Rect(point.x + 74, point.y + 40, 250, 54), ChipCount(seat.stack) + "\n" + "BANKROLL", _text);
                }
            }
            else
            {
                var spacing = Mathf.Min(70, 266f / Math.Max(1, round.dealer.Length - 1));
                var startX = 556 - (72 + (round.dealer.Length - 1) * spacing) / 2;
                for (var i = 0; i < round.dealer.Length; i++)
                    PlayingCard(new Rect(startX + i * spacing, 150, 72, 100), round.dealer[i], round.id + ":dealer:" + i, i);
                Chips(new Vector2(420, 132), round.hands.Sum(h => (long)h.bet), 3);
                Label(new Rect(380, 260, 350, 26), round.phase == "complete" ? "DEALER " + round.dealerTotal : "Dealer’s card is face down", _smallCenter);
                for (var i = 0; i < round.hands.Length; i++)
                {
                    var hand = round.hands[i];
                    var location = BlackjackPosition(i, round.hands.Length);
                    var x = location.x;
                    var y = location.y;
                    var mine = table.seats.Any(seat => seat.mine && seat.position == hand.position);
                    var active = round.phase == "playing" && round.turn == hand.position;
                    SeatPanel(new Rect(x, y, 344, 184), hand.position, hand.name, mine, active, round.phase == "complete" && hand.profit > 0);
                    for (var c = 0; c < hand.cards.Length; c++)
                        PlayingCard(new Rect(x + 74 + c * Mathf.Min(46, 142f / Math.Max(1, hand.cards.Length - 1)), y + 42, 64, 88), hand.cards[c], round.id + ":" + hand.position + ":" + c, c);
                    Badge(new Rect(x + 284, y + 42, 48, 32), hand.total.ToString(), active);
                    Chips(new Vector2(x + 307, y + 116), hand.bet, 2);
                    var seated = table.seats.FirstOrDefault(seat => seat.position == hand.position);
                    var stack = seated?.stack ?? 0;
                    Fill(new Rect(x + 8, y + 146, 328, 30), _panel);
                    Label(new Rect(x + 14, y + 149, 74, 24), "BET " + hand.bet, _valueLeft);
                    Label(new Rect(x + 94, y + 149, 146, 24), round.phase == "complete" ? BlackjackStatus(hand.status) + " " + (hand.profit > 0 ? "+" : "") + hand.profit : active ? (mine ? "YOUR TURN" : "PLAYING") : BlackjackStatus(hand.status), _valueLeft);
                    if (!hand.npc)
                        Label(new Rect(x + 246, y + 149, 84, 24), seated == null ? "LEFT" : "STACK " + stack, _valueRight);
                }
            }
            var ownSeat = table.seats.FirstOrDefault(seat => seat.mine);
            var ownHand = round?.hands.FirstOrDefault(hand => hand.position == ownSeat?.position);
            var playing = round?.phase == "playing";
            RefreshBlackjackBet();
            TableStatus(playing ? "Turn: " + round.hands.First(hand => hand.position == round.turn).name + " · " + Math.Max(0, round.deadline - (_tables.serverTime + (long)(Time.unscaledTime - _tablesReceivedAt))) + "s" : _blackjackBet.Maximum < BlackjackBetSelection.Minimum ? "Not enough table chips. Cash out to change your bankroll." : !_blackjackBet.Valid ? "Enter an even wager within your table bankroll." : "Choose a wager, then deal.", playing ? "Timeout: stand" : "Even wagers · Exact 3:2 · Buttons ±2");
            if (playing)
            {
                GUI.enabled = TableControls && ownHand != null && round.turn == ownHand.position;
                if (Button(new Rect(0, 608, 170, 44), "HIT", primary: true))
                    BeginTableAction("hit");
                if (Button(new Rect(182, 608, 170, 44), "STAND"))
                    BeginTableAction("stand");
                GUI.enabled = GUI.enabled && ownHand.cards.Length == 2 && ownSeat.stack >= ownHand.bet * 2L;
                if (Button(new Rect(364, 608, 170, 44), "DOUBLE"))
                    BeginTableAction("double");
            }
            else
            {
                GUI.enabled = TableControls && _blackjackBet.Maximum >= BlackjackBetSelection.Minimum && (_blackjackBet.Value > BlackjackBetSelection.Minimum || !_blackjackBet.Valid);
                if (Button(new Rect(0, 608, 44, 44), "-")) _blackjackBet.Step(-1);
                GUI.enabled = TableControls && _blackjackBet.Maximum >= BlackjackBetSelection.Minimum;
                _blackjackBet.Text = QuantityField(new Rect(54, 608, 154, 44), _blackjackBet.Text, 10);
                GUI.enabled = TableControls && _blackjackBet.Maximum >= BlackjackBetSelection.Minimum && (_blackjackBet.Value < _blackjackBet.Maximum || !_blackjackBet.Valid);
                if (Button(new Rect(218, 608, 44, 44), "+")) _blackjackBet.Step(1);
                GUI.enabled = TableControls && _blackjackBet.Valid;
                if (Button(new Rect(278, 608, 170, 44), "DEAL", primary: true))
                    BeginTableAction("start", amount: _blackjackBet.Value);
            }
            GUI.enabled = TableControls;
            if (Button(new Rect(738, 608, 180, 44), "CASINO HOME"))
                Navigate(0);
            if (Button(new Rect(930, 608, 180, 44), playing ? "LEAVE AFTER HAND" : "CASH OUT"))
                BeginTableAction("leave");
            GUI.enabled = true;
            DrawTableRecovery();
        }
        private void DrawTableRecovery(bool intro = false)
        {
            if (_tablePending != null)
            {
                GUI.enabled = !_tableSending && !_tablePolling && !_busy && _pending == null;
                if (Button(intro ? new Rect(740, 608, 346, 44) : new Rect(580, 608, 146, 44), "RESUME"))
                    StartCoroutine(Guard(SendTableAction()));
                GUI.enabled = true;
            }
            if (_tableError != null)
                _message = _tableError;
        }
    }
}
