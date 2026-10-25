using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private int _page;
        private float _pageAt;
        private static readonly string[] PageNames = { "CASINO HOME", "SLOTS", "DEPOSIT / WITHDRAW", "BOSS RACING", "SETTINGS", "", "BLACKJACK", "POKER", "RUSSIAN ROULETTE", "ROULETTE" };
        private static readonly string[] CatalogueTitles = { "SCAVENGER’S LUCK", "BOSS RACING", "BLACKJACK", "TEXAS HOLD’EM", "EUROPEAN ROULETTE", "RUSSIAN ROULETTE" };
        private static readonly string[] CatalogueDescriptions = { "Three reels. Six symbols.", "Back a boss on the Factory circuit.", "Beat the dealer. Blackjack pays 3:2.", "Texas Hold’em against up to four rivals.", "A single-zero wheel. Place your bets.", "Six chambers. One winner." };
        private static readonly int[] CataloguePages = { 1, 3, 6, 7, 9, 8 };
        private static readonly string[] FallbackSymbols = { "7", "DIAMOND", "BAR", "BELL", "CHERRY", "LEMON" };
        private static readonly int[] FallbackTriples = { 100, 50, 25, 15, 10, 2 };
        private readonly Texture2D[] _symbols = new Texture2D[6];
        private readonly Color _gold = new Color(.79f, .66f, .43f), _ink = new Color(.031f, .037f, .038f), _panel = new Color(.056f, .067f, .068f), _line = new Color(.15f, .17f, .17f);
        private GUIStyle _heading, _muted, _number, _barLeft, _barRight, _chipLabel, _smallCenter, _valueLeft, _valueRight;
        private void LoadCasinoAssets()
        {
            var directory = Path.Combine(Path.GetDirectoryName(Info.Location), "assets");
            for (var i = 0; i < 6; i++)
                _symbols[i] = LoadCasinoTexture(directory, "symbol-" + i + ".png", FilterMode.Bilinear);
            for (var i = 0; i < FactoryFiles.Length; i++)
                _factoryLayers[i] = LoadCasinoTexture(directory, FactoryFiles[i], FilterMode.Point);
            LoadRacingAssets(directory);
            _backroomTable = LoadCasinoTexture(directory, "backroom-table.png", FilterMode.Bilinear);
            _revolverArt = LoadCasinoTexture(directory, "revolver.png", FilterMode.Bilinear, false);
            _revolverArtwork = new RevolverArtwork(_revolverArt);
            Destroy(_revolverArt);
            _revolverArt = null;
            _slotCabinet = LoadCasinoTexture(directory, "slot-cabinet.png", FilterMode.Bilinear);
            _cardAtlas = LoadCasinoTexture(directory, "cards.png", FilterMode.Bilinear);
            _tableArt = LoadCasinoTexture(directory, "table-felt.png", FilterMode.Bilinear);
            _chipIcon = LoadCasinoTexture(directory, "chip.png", FilterMode.Bilinear);
            _portraitAtlas = LoadCasinoTexture(directory, "portraits.png", FilterMode.Bilinear);
        }
        private Texture2D LoadCasinoTexture(string directory, string file, FilterMode filter, bool discardPixels = true)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(directory, file)), discardPixels))
                    throw new IOException("Invalid casino artwork: " + file);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = filter;
                return texture;
            }
            catch { Destroy(texture); throw; }
        }
        private void LoadRacingAssets(string directory)
        {
            for (var i = 0; i < BossIds.Length; i++)
                _runnerSheets[i] = LoadCasinoTexture(directory, "boss-" + BossIds[i] + ".png", FilterMode.Point);
        }
        private void CasinoStyles()
        {
            if (_heading != null)
                return;
            _heading = new GUIStyle(_text) { fontSize = Ui.HeadingSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _heading.normal.textColor = new Color(.95f, .94f, .90f);
            _muted = new GUIStyle(_text) { fontSize = Ui.MetaSize };
            _muted.normal.textColor = new Color(.62f, .66f, .70f);
            _number = new GUIStyle(_heading) { fontSize = Ui.NumberSize, alignment = TextAnchor.MiddleRight };
            _section = new GUIStyle(_heading) { fontSize = Ui.SectionSize };
            _barLeft = new GUIStyle(_muted) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _barRight = new GUIStyle(_barLeft) { alignment = TextAnchor.MiddleRight };
            _valueLeft = new GUIStyle(_barLeft) { wordWrap = true };
            _valueRight = new GUIStyle(_barRight) { wordWrap = true };
            _chipLabel = new GUIStyle(_barLeft) { alignment = TextAnchor.MiddleRight };
            _smallCenter = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            _navLabel = new GUIStyle(_text) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _navigationLabel = new GUIStyle(_navLabel) { fontSize = Ui.MetaSize };
            _sectionLabel = new GUIStyle(_barLeft) { fontSize = 12 };
            _sectionLabel.normal.textColor = _gold;
            _inputLabel = new GUIStyle(_text) { fontSize = Ui.InputSize, alignment = TextAnchor.MiddleLeft, wordWrap = false };
        }
        private void DrawCasino()
        {
            CasinoStyles();
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.006f, .009f, .014f, 1));
            var scale = Math.Min(Screen.width / 1500f, Screen.height / 960f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            BeginGroup(new Rect((Screen.width / scale - Ui.Width) / 2, (Screen.height / scale - Ui.Height) / 2, Ui.Width, Ui.Height));
            Fill(new Rect(-8, -8, 1456, 916), new Color(.075f, .078f, .081f));
            Fill(new Rect(-3, -3, 1446, 902), new Color(.008f, .009f, .011f));
            Fill(new Rect(0, 8, 1440, 892), Color.black);
            Fill(new Rect(0, 0, 1440, 892), _ink);
            Fill(new Rect(0, 0, Ui.Width, 40), new Color(.075f, .083f, .083f));
            Label(new Rect(32, 0, 1050, 40), "RIPPERS CASINO", _barLeft);
            var exit = new Rect(1240, 4, 152, 32);
            var inputEnabled = GUI.enabled;
            GUI.enabled = _open;
            if (Button(exit, "EXIT")) Close();
            GUI.enabled = inputEnabled && _open;
            Fill(new Rect(exit.x, exit.yMax - 2, exit.width, 2), new Color(.46f, .19f, .17f));
            Fill(new Rect(0, 40, Ui.Width, 104), new Color(.045f, .054f, .055f));
            Fill(new Rect(0, 144, 1440, 1), _line);
            DrawTexture(new Rect(32, 64, 56, 56), _moduleIcon, ScaleMode.ScaleToFit);
            Label(new Rect(112, 72, 600, 40), "RIPPERS CASINO", _heading);
            Label(new Rect(824, 60, 344, 24), "AVAILABLE CHIPS", _chipLabel);
            WalletNumber(new Rect(824, 84, 344, 44), _shownBalance);
            Fill(new Rect(1184, 64, 1, 56), _line);
            if (Button(new Rect(1200, 68, 192, Ui.ButtonHeight), "DEPOSIT / WITHDRAW"))
                Navigate(2);
            DrawNavigation();
            var oldTint = GUI.color;
            var pageFade = Mathf.SmoothStep(0, 1, (Time.unscaledTime - _pageAt) / .16f);
            GUI.color = new Color(oldTint.r, oldTint.g, oldTint.b, oldTint.a * pageFade);
            BeginGroup(new Rect(Ui.PageX, Ui.PageY + (1 - pageFade) * 8, Ui.PageWidth, Ui.PageHeight));
            if (_page == 0)
                CasinoHome();
            else if (_page == 1)
                CasinoSlots();
            else if (_page == 2)
                CasinoWallet();
            else if (_page == 3)
                CasinoRacing();
            else if (_page == 4)
                CasinoSettings();
            else if (_page == 6)
                CasinoBlackjack();
            else if (_page == 7)
                CasinoPoker();
            else if (_page == 9)
                CasinoRoulette();
            else if (_page == 8)
                CasinoElimination();
            else
                CasinoHome();
            EndGroup();
            GUI.color = oldTint;
            Fill(new Rect(0, 857, 1440, 1), _line);
            Label(new Rect(32, 860, 1100, 32), _autoSpin.Active ? "AUTO  /  " + _message : _page >= 6 && _tableActionError != null ? _tableActionError : _message, _small);
            if (_pending != null && !_restart)
            {
                GUI.enabled = !_busy && !_refreshing && !GameActive && (!_requestSent || _pendingFromDisk);
                if (Button(new Rect(1158, 862, 247, 25), "RESUME PAYMENT", _small))
                    StartCoroutine(Guard(Submit()));
                GUI.enabled = true;
            }
            EndGroup();
            GUI.matrix = Matrix4x4.identity;
        }
        private void Navigate(int page)
        {
            if (!(_laptopSession?.Ready ?? true))
                return;
            if (_page != page)
            {
                if (_page == 1) _autoSpin.Stop();
                _page = page;
                _pageAt = Time.unscaledTime;
                Play(CasinoCue.Button);
            }
        }
        private void CasinoHome()
        {
            Label(new Rect(0, 0, 1080, 39), "Choose your game", _heading);
            Label(new Rect(1, 45, 1080, 28), "Play the house or take a seat with a rival.", _muted);
            Fill(new Rect(0, 86, 1110, 1), _line);
            Label(new Rect(0, 86, 800, 28), "GAMES", _sectionLabel);
            for (var card = 0; card < 6; card++)
            {
                var x = card % 3 * 378;
                var y = 126 + card / 3 * 264;
                var area = new Rect(x, y, 354, 250);
                var hover = area.Contains(Event.current.mousePosition);
                Fill(new Rect(x, y + 3, 354, 250), new Color(0, 0, 0, .28f));
                Fill(area, hover ? new Color(.085f, .098f, .096f) : _panel);
                Outline(area, hover ? _gold * .7f : _line);
                Fill(new Rect(x + 1, y + 1, 352, 108), new Color(.025f, .038f, .038f));
                if (card == 0)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        Fill(new Rect(x + 68 + i * 74, y + 13, 66, 82), new Color(.82f, .79f, .69f));
                        DrawTexture(new Rect(x + 72 + i * 74, y + 22, 58, 64), _symbols[i], ScaleMode.ScaleToFit);
                    }
                }
                else if (card == 1)
                {
                    IndustrialStrip(new Rect(x + 1, y + 1, 352, 108), 0);
                    DrawRunner(new Rect(x + 65, y + 4, 96, 96), 0, 0);
                    DrawRunner(new Rect(x + 186, y + 4, 96, 96), 1, 2);
                }
                else if (card == 2 || card == 3)
                {
                    PlayingCard(new Rect(x + 104, y + 10, 62, 86), card == 2 ? 0 : 39, "home:" + card + ":a");
                    PlayingCard(new Rect(x + 176, y + 14, 62, 86), card == 2 ? 51 : 50, "home:" + card + ":b", 1);
                    Chips(new Vector2(x + 266, y + 84), 5, 4);
                }
                else if (card == 4)
                    RouletteWheel(new Rect(x + 120, y + 2, 110, 110), -1, 1);
                else
                    VirtualRevolver(new Rect(x + 85, y + 4, 188, 106), 0);
                Label(new Rect(x + 18, y + 116, 318, 29), CatalogueTitles[card], _section);
                Label(new Rect(x + 18, y + 149, 318, 26), CatalogueDescriptions[card], _muted);
                Label(new Rect(x + 18, y + 180, 318, 21), card < 2 ? "PLAY THE HOUSE" : "TAKE A SEAT", _sectionLabel);
                if (Button(new Rect(x + 18, y + 207, 318, 32), card < 2 ? "PLAY  →" : "TAKE A SEAT  →", primary: hover))
                    Navigate(CataloguePages[card]);
            }
        }
        private void CasinoSlots()
        {
            Label(new Rect(0, 0, 755, 40), "Scavenger's Luck", _heading);
            Label(new Rect(0, 48, 755, 28), "Three reels · One payline · Up to 100× your wager", _muted);
            DrawTexture(new Rect(0, 98, 765, 373), _slotCabinet);
            var winning = _presentation.Revealed && _presentation.VisiblePayout > 0 && Time.unscaledTime - _celebrateAt < 3;
            if (winning)
            {
                var elapsed = Time.unscaledTime - _celebrateAt;
                var glow = _jackpotReveal ? .12f + .04f * Mathf.Sin(elapsed * 3) : .18f + .08f * Mathf.Sin(elapsed * 10);
                Fill(new Rect(7, 105, 751, 359), new Color(_jackpotReveal ? .9f : .35f, .63f, .22f, glow));
            }
            for (var i = 0; i < 3; i++)
                CasinoReel(i, new Rect(23 + i * 242, 125, 234, 319));
            Fill(new Rect(12, 282, 741, 2), winning ? new Color(.91f, .78f, .40f) : new Color(.64f, .46f, .18f, .65f));
            Label(new Rect(14, 272, 30, 25), ">", _small);
            Label(new Rect(728, 272, 30, 25), "<", _small);
            if (JackpotCelebrating) DrawJackpot();
            Fill(new Rect(0, 490, 765, 74), _panel);
            Label(new Rect(20, 499, 230, 22), "WAGER", _muted);
            Label(new Rect(20, 525, 260, 31), ChipCount(_state?.bets[Math.Min(_bet, _state.bets.Length - 1)] ?? 1), _text);
            var ratio = _presentation.VisiblePayout / (double)Math.Max(1, _state?.bets[_bet] ?? 1);
            Label(new Rect(411, 499, 331, 22), !winning ? "LAST PAYOUT" : _jackpotReveal ? "JACKPOT" : ratio >= 20 ? "LARGE WIN" : ratio >= 5 ? "NICE WIN" : "WIN", _muted);
            Label(new Rect(411, 521, 331, 35), !_presentation.Revealed ? "—" : ChipCount(_presentation.VisiblePayout), _number);
            GUI.enabled = Available && !_autoSpin.Active && !_autoSpin.InFlight;
            _bet = StakeSelector(new Rect(0, 584, 390, Ui.ButtonHeight), _bet);
            GUI.enabled = JackpotCelebrating ? _open && !_busy && !_restart : Available && !_autoSpin.Active && !_autoSpin.InFlight && _state != null && _shownBalance >= _state.bets[_bet];
            if (Button(new Rect(406, 584, 174, Ui.ButtonHeight), JackpotCelebrating ? "CONTINUE" : _busy || _presentation.Active ? "SPINNING…" : "SPIN", primary: true))
            {
                if (JackpotCelebrating) _celebrateAt = Time.unscaledTime - (float)JackpotPresentation.Duration;
                else Begin("spin", _state.bets[_bet]);
            }
            GUI.enabled = true;
            GUI.enabled = _autoSpin.Active || Available && !_autoSpin.InFlight && _state != null && _shownBalance >= _state.bets[_bet];
            if (Button(new Rect(592, 584, 172, Ui.ButtonHeight), _autoSpin.Active ? "STOP AUTO" : "AUTO", selected: _autoSpin.Active))
            {
                if (_autoSpin.Active) _autoSpin.Stop();
                else _autoSpin.Start(Available);
            }
            GUI.enabled = true;
            Fill(new Rect(792, 98, 318, 532), _panel);
            Label(new Rect(813, 112, 275, 37), "PAYOUT TABLE", _section);
            Fill(new Rect(812, 156, 275, 1), _line);
            Label(new Rect(813, 164, 275, 23), "THREE MATCHING SYMBOLS", _muted);
            for (var i = 0; i < 6; i++)
            {
                var y = 190 + i * 54;
                DrawTexture(new Rect(814, y, 37, 37), _symbols[i], ScaleMode.ScaleToFit);
                Label(new Rect(865, y + 5, 120, 30), (_state?.symbols ?? FallbackSymbols)[i], _small);
                Label(new Rect(995, y + 5, 91, 30), ((_state?.triples != null ? _state.triples[i] : FallbackTriples[i])) + "x", _center);
            }
            Fill(new Rect(812, 525, 275, 1), _line);
            Label(new Rect(814, 540, 275, 39), "EXACTLY TWO CHERRIES  /  " + (_state?.twoCherries ?? 2) + "x", _small);
            Label(new Rect(814, 587, 275, 32), "Payouts include your wager.", _muted);
        }
        private void DrawJackpot()
        {
            var elapsed = Time.unscaledTime - _celebrateAt;
            var fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .16f)) * Mathf.Clamp01(((float)JackpotPresentation.Duration - elapsed) / .35f);
            var tint = GUI.color;
            GUI.color *= new Color(1, 1, 1, fade);
            for (var inset = 0; inset < 4; inset++)
                Outline(new Rect(7 + inset * 2, 105 + inset * 2, 751 - inset * 4, 359 - inset * 4), new Color(.83f, .65f, .32f, .28f / (inset + 1)));
            Fill(new Rect(95, 101, 575, 46), new Color(.06f, .055f, .036f, .96f));
            Fill(new Rect(115, 102, 535, 2), _gold);
            var heading = _heading.normal.textColor;
            _heading.normal.textColor = new Color(.95f, .81f, .48f);
            var alignment = _heading.alignment;
            _heading.alignment = TextAnchor.MiddleCenter;
            Label(new Rect(115, 106, 535, 36), "JACKPOT", _heading);
            _heading.alignment = alignment;
            _heading.normal.textColor = heading;
            Fill(new Rect(95, 397, 575, 58), new Color(.045f, .043f, .031f, .97f));
            Fill(new Rect(115, 452, 535, 2), _gold);
            var numberColor = _number.normal.textColor;
            var numberAlignment = _number.alignment;
            _number.normal.textColor = new Color(.96f, .84f, .57f);
            _number.alignment = TextAnchor.MiddleCenter;
            var amount = ChipCount(_presentation.VisiblePayout);
            Label(new Rect(115, 403, 535, 44), amount, _number);
            _number.alignment = numberAlignment;
            _number.normal.textColor = numberColor;
            var sweep = Mathf.Clamp01(elapsed / 1.6f);
            Fill(new Rect(23, 462, 719 * sweep, 2), new Color(.96f, .80f, .45f, .65f));
            GUI.color = tint;
        }
        private void CasinoReel(int reel, Rect rect)
        {
            Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(.43f, .36f, .24f));
            Fill(rect, new Color(.88f, .86f, .80f));
            BeginGroup(rect);
            var position = ReelPosition(reel);
            if (_presentation.Resolved && _presentation.Stopped(reel, PresentationElapsed))
            {
                var stop = reel == 0 ? SpinPresentation.FirstStop : reel == 1 ? SpinPresentation.SecondStop : SpinPresentation.LastStop;
                var settle = (float)(PresentationElapsed - stop);
                if (settle < .18f) position += .035f * Math.Sin(settle / .18f * Math.PI * 2) * (1 - settle / .18f);
            }
            var step = (int)Math.Floor(position);
            var fraction = (float)(position - step);
            for (var row = -1; row <= 2; row++)
            {
                var index = ((step + row) % 6 + 6) % 6;
                var area = new Rect(19, 56 + (row - fraction) * 204, 196, 204);
                if (_presentation.Active && !_presentation.Stopped(reel, PresentationElapsed))
                {
                    var tint = GUI.color;
                    GUI.color = new Color(1, 1, 1, .15f);
                    DrawTexture(new Rect(area.x, area.y - 14, area.width, area.height), _symbols[index], ScaleMode.ScaleToFit);
                    DrawTexture(new Rect(area.x, area.y + 14, area.width, area.height), _symbols[index], ScaleMode.ScaleToFit);
                    GUI.color = tint;
                }
                if (JackpotCelebrating && row == 0)
                {
                    var elapsed = Time.unscaledTime - _celebrateAt;
                    var lift = Mathf.Sin(Mathf.Clamp01(elapsed / .38f) * Mathf.PI) * 5;
                    area.y -= lift;
                    var growth = 1 + .025f * Mathf.Sin(elapsed * 3.6f);
                    area = new Rect(area.center - area.size * growth / 2, area.size * growth);
                    Outline(new Rect(9, 49, rect.width - 18, 218), new Color(.80f, .61f, .28f, .38f));
                }
                DrawTexture(area, _symbols[index], ScaleMode.ScaleToFit);
            }
            for (var i = 0; i < 12; i++)
            {
                var alpha = .035f + (12 - i) * .017f;
                Fill(new Rect(0, i * 4, rect.width, 4), new Color(.025f, .03f, .035f, alpha));
                Fill(new Rect(0, rect.height - (i + 1) * 4, rect.width, 4), new Color(.025f, .03f, .035f, alpha));
            }
            Fill(new Rect(0, 0, 7, rect.height), new Color(0, 0, 0, .12f));
            Fill(new Rect(rect.width - 7, 0, 7, rect.height), new Color(0, 0, 0, .12f));
            EndGroup();
        }
        private void WalletNumber(Rect area, long value)
        {
            var text = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            var size = _number.fontSize;
            var width = _number.CalcSize(new GUIContent(text)).x;
            if (width > area.width) _number.fontSize = Math.Max(14, (int)(size * area.width / width));
            Label(area, text, _number);
            _number.fontSize = size;
        }
        private static string CashNumber(long amount) => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        private void CasinoWallet()
        {
            Label(new Rect(0, 0, 1080, 40), "The cashier", _heading);
            Label(new Rect(1, 49, 1080, 35), "1 CHIP = 1,000 RUB · Exchange currency and chips · No fees", _muted);
            Fill(new Rect(0, 108, 1110, 101), _panel);
            Chips(new Vector2(52, 178), 8, 5);
            Label(new Rect(88, 128, 530, 24), "AVAILABLE CHIPS", _barLeft);
            Label(new Rect(88, 164, 530, 26), "Cash reserve  " + CashNumber(_state?.remainderRub ?? 0) + " RUB", _muted);
            WalletNumber(new Rect(648, 128, 438, 48), _shownBalance);
            Fill(new Rect(0, 232, 709, 348), _panel);
            Fill(new Rect(24, 402, 660, 1), _line);
            Label(new Rect(24, 248, 650, 27), "CURRENCY", _muted);
            GUI.enabled = Available;
            var previous = _currency;
            _currency = CurrencyButtons(new Rect(24, 288, 660, Ui.ButtonHeight), _currency);
            if (previous != _currency) _depositQuantity = _currency == 0 ? "100000" : "100";
            GUI.enabled = true;
            var currency = _currencies[_currency];
            ExchangeRate rate = null;
            _state?.exchange?.TryGetValue(currency, out rate);
            var compatible = _state?.economyVersion == 4 && _state.cashierVersion == 3 && rate != null && rate.rubPerUnit > 0;
            Label(new Rect(24, 350, 660, 44), compatible ? "1 " + currency + " = " + rate.rubPerUnit + " RUB" + "\nRemaining value stays in your cash reserve." : "Cashier unavailable. Check the casino installation.", _text);
            Label(new Rect(24, 412, 316, 24), "DEPOSIT / " + currency, _barLeft);
            Label(new Rect(364, 412, 320, 24), "WITHDRAW / WHOLE CHIPS", _barLeft);
            GUI.enabled = Available && compatible;
            _depositQuantity = QuantityField(new Rect(24, 442, 316, 44), _depositQuantity, 19);
            _withdrawQuantity = QuantityField(new Rect(364, 442, 320, 44), _withdrawQuantity, 19);
            GUI.enabled = Available && compatible && rate.maxDepositUnits > 0;
            if (Button(new Rect(24, 494, 316, 30), "MAX DEPOSIT")) _depositQuantity = rate.maxDepositUnits.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GUI.enabled = Available && compatible && _state.redemptionEnabled && rate.maxWithdrawalUnits > 0;
            if (Button(new Rect(364, 494, 320, 30), "MAX WITHDRAW")) _withdrawQuantity = _state.balance.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var reserve = _state?.remainderRub ?? 0;
            var deposit = CashierAmount.Deposit(_depositQuantity, compatible ? rate.rubPerUnit : 0, rate?.maxDepositUnits ?? 0, reserve);
            var withdrawal = CashierAmount.Withdrawal(_withdrawQuantity, compatible ? rate.rubPerUnit : 0, _state?.balance ?? 0, rate?.maxWithdrawalUnits ?? 0, reserve);
            GUI.enabled = Available && deposit.Valid;
            if (Button(new Rect(24, 536, 316, 44), _busy ? "PROCESSING…" : "DEPOSIT", primary: true)) Begin("buy", deposit.Amount);
            GUI.enabled = Available && _state.redemptionEnabled && withdrawal.Valid;
            if (Button(new Rect(364, 536, 320, 44), _busy ? "PROCESSING…" : "WITHDRAW")) Begin("redeem", withdrawal.Amount);
            GUI.enabled = true;
            Fill(new Rect(735, 232, 375, 348), _panel);
            Label(new Rect(759, 248, 329, 31), "STASH FUNDS", _section);
            for (var i = 0; i < 3; i++)
            {
                var c = _currencies[i];
                Label(new Rect(760, 312 + i * 64, 80, 40), c, _barLeft);
                WalletNumber(new Rect(840, 312 + i * 64, 244, 40), _state?.currencyBalances?.TryGetValue(c, out var v) == true ? v : 0);
            }
            Label(new Rect(760, 508, 328, 48), "Withdrawals include your reserve.\nLeave space in your stash for payment.", _muted);
            var depositHint = !compatible ? "Cashier unavailable." : rate.maxDepositUnits == 0 ? "No " + currency + " available to deposit." : "Enter " + currency + " to deposit. Maximum: " + CashNumber(rate.maxDepositUnits) + ".";
            var withdrawalHint = !compatible ? "Cashier unavailable." : _state.balance == 0 && reserve == 0 ? "No chips to withdraw." : _state.balance == 0 && rate.maxWithdrawalUnits == 0 ? "Reserve too small for 1 " + currency + ". Choose RUB." : "Available: " + ChipCount(_state.balance) + ".\nUse 0 to withdraw only the reserve.";
            Label(new Rect(0, 594, 709, 56), deposit.Valid ? CashNumber(deposit.CurrencyUnits) + " " + currency + " → " + ChipCount(deposit.Chips) + "\nReserve after deposit: " + deposit.RemainderRub + " RUB" : depositHint, _muted);
            Label(new Rect(735, 594, 375, 56), withdrawal.Valid ? ChipCount(withdrawal.Chips) + " + " + reserve + " RUB → " + CashNumber(withdrawal.CurrencyUnits) + " " + currency + "\nReserve after withdrawal: " + withdrawal.RemainderRub + " RUB" : withdrawalHint, _muted);
            if (_state?.tableHeld > 0) Label(new Rect(0, 654, 1110, 24), "AT THE TABLE / " + ChipCount(_state.tableHeld) + " / Cash out to exchange these chips.", _small);
        }
        private void CasinoSettings()
        {
            Label(new Rect(0, 0, 1080, 40), "Casino settings", _heading);
            Label(new Rect(0, 49, 1080, 35), "Sound, ambience and your saved game.", _muted);
            Fill(new Rect(0, 110, 1110, 185), _panel);
            Label(new Rect(25, 132, 710, 35), "CASINO SOUND", _section);
            Label(new Rect(24, 184, 688, 64), "Adjust casino sound.\nYour Tarkov master volume also applies.", _muted);
            if (Button(new Rect(776, 144, 104, Ui.ButtonHeight), "-"))
            {
                _volume.Value = Mathf.Max(0, _volume.Value - .1f);
                Config.Save();
                Play(CasinoCue.Button);
            }
            Label(new Rect(888, 144, 104, Ui.ButtonHeight), (_volume.Value * 100).ToString("0") + "%", _center);
            if (Button(new Rect(1000, 144, 80, Ui.ButtonHeight), "+"))
            {
                _volume.Value = Mathf.Min(1, _volume.Value + .1f);
                Config.Save();
                Play(CasinoCue.Button);
            }
            if (Button(new Rect(776, 208, 304, Ui.ButtonHeight), _audioMuted.Value ? "UNMUTE" : "MUTE"))
            {
                _audioMuted.Value = !_audioMuted.Value;
                Config.Save();
            }
            Fill(new Rect(0, 315, 1110, 90), _panel);
            Label(new Rect(24, 328, 710, 32), "HIDEOUT AMBIENCE", _text);
            Label(new Rect(24, 368, 1056, 24), "Hideout ambience quietens while the casino is open and returns when you leave.", _muted);
            Fill(new Rect(0, 426, 1110, 211), _panel);
            Label(new Rect(24, 448, 1056, 32), "YOUR GAME", _section);
            Label(new Rect(24, 496, 950, 112), "Your balance and results are saved automatically. Closing the casino does not cancel placed bets.\n\nIf a payment is interrupted, reopen the casino and choose Resume. Exit or Esc returns to the hideout.", _muted);
        }
    }
}
