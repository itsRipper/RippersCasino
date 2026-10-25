using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Comfort.Common;
using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private void LoadPending()
        {
            string key;
            using (var hash = SHA256.Create())
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(RequestHandler.SessionId ?? "unknown"))).Replace("-", "").Substring(0, 24);
            _pendingPath = Path.Combine(BepInEx.Paths.ConfigPath, "RippersCasino", "pending-" + key + ".json");
            if (!File.Exists(_pendingPath))
                return;
            _pending = JsonConvert.DeserializeObject<CasinoRequest>(File.ReadAllText(_pendingPath));
            if (_pending == null || _pending.economyVersion != 4 || !Guid.TryParse(_pending.transactionId, out _) || _pending.sequence < 1 || !new[] { "spin", "race", "buy", "redeem" }.Contains(_pending.operation))
                throw new InvalidDataException("Invalid recovery request.");
            _pendingFromDisk = true;
        }
        private IEnumerator Guard(IEnumerator operation)
        {
            try
            {
                while (true)
                {
                    bool moved;
                    object value = null;
                    try
                    {
                        moved = operation.MoveNext();
                        if (moved)
                            value = operation.Current;
                    }
                    catch (Exception error)
                    {
                        Logger.LogError("[RippersCasino] Transaction/UI recovered from exception: " + error);
                        StopAutoOnError();
                        _busy = false;
                        _casinoAudio?.StopSpin();
                        _presentation.Reject(_shownBalance);
                        _racePresentation.Reject(_shownBalance);
                        if (_pending != null && _requestSent)
                            RequireRestart("Payment interrupted. Restart Tarkov to refresh your stash.");
                        else
                            _message = "Casino interrupted. No payment was sent. Reopen to try again.";
                        Close(true);
                        yield break;
                    }
                    if (!moved)
                        yield break;
                    yield return value;
                }
            }
            finally
            {
                (operation as IDisposable)?.Dispose();
            }
        }
        private bool _refreshing;
        private static void ObserveReadFailure(Task task)
        {
            // SPT's HTTP helper has no cancellation token; observe faults after a read is abandoned.
            task.ContinueWith(failed => { var ignored = failed.Exception; },
                System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        private IEnumerator Refresh()
        {
            var generation = _readSession.Generation;
            while (_refreshing && _readSession.Accept(generation)) yield return null;
            if (!_readSession.CanPoll(HideoutScreenVisible) || !_readSession.Accept(generation) || _busy || GameActive) yield break;
            _refreshing = true;
            try
            {
                _message = "Loading…";
                var task = RequestHandler.GetJsonAsync("/ripperscasino/state");
                ObserveReadFailure(task);
                var end = Time.unscaledTime + 25;
                while (_readSession.Accept(generation) && !task.IsCompleted && Time.unscaledTime < end)
                    yield return null;
                if (!_readSession.Accept(generation)) yield break;
                if (!task.IsCompleted)
                    _message = "Casino unavailable. Close and reopen to try again.";
                else if (ReadState(task) && _pending != null)
                {
                    var receipt = MatchingReceipt();
                    if (_pendingFromDisk && receipt != null)
                    {
                        ClearPending();
                        _message = PlayerMessage(receipt.error) ?? "Previous payment restored.";
                    }
                    else if (_state.sequence > _pending.sequence)
                        RequireRestart("Your payment could not be confirmed. Restart Tarkov.");
                    else
                        _message = "A payment is waiting. Choose Resume to finish it.";
                }
            }
            finally { _refreshing = false; }
        }
        private CasinoReceipt MatchingReceipt() => _state?.last?.id == _pending?.transactionId ? _state.last : null;
        private string PlayerMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            if (message == "Arithmetic operation resulted in an overflow.") return "That amount is too large. Choose a smaller amount.";
            if (message.Length > 240 || message.Contains(":\\") || message.Contains(":/") || message.Contains("RippersCasino.") || message.Contains("SPTarkov.") || message.Contains("BytePositionInLine") || message.Contains("System."))
            {
                Logger.LogWarning("[RippersCasino] Unavailable game: " + message);
                return "Your saved game could not be opened. Close the casino and check the log.";
            }
            return message;
        }
        private bool ReadState(Task<string> task)
        {
            try
            {
                if (!task.IsCompleted) throw new InvalidOperationException("Casino state has not completed.");
                var state = JsonConvert.DeserializeObject<CasinoState>(task.GetAwaiter().GetResult());
                if (state == null || state.economyVersion != 4 || state.cashierVersion != 3 || state.chipRubles != 1000 || state.symbols?.Length != 6 || state.bets == null || state.bets.Length == 0 || state.bets.Any(x => x <= 0))
                    throw new InvalidDataException("Invalid state.");
                _state = state;
                _bet = Math.Min(_bet, state.bets.Length - 1);
                if (!GameActive)
                {
                    _shownBalance = state.balance;
                    var slot = state.lastSlot;
                    if (slot?.reels?.Length == 3)
                        for (var i = 0; i < 3; i++)
                            _restSymbols[i] = SymbolIndex(slot.reels[i]);
                    _presentation.SetRest(_restSymbols, state.symbols.Length);
                    _lastRace = state.lastRace;
                }
                _raceBet = Math.Min(_raceBet, state.bets.Length - 1);
                _message = PlayerMessage(state.error) ?? (!state.enabled ? "CASINO CLOSED" : !state.available ? "Requires Generator level 1 and Intelligence Center level 1." : "");
                return true;
            }
            catch (Exception ex) { StopAutoOnError(); _message = "Casino unavailable. Check the installation and try again."; Logger.LogWarning("[RippersCasino] State unavailable: " + ex.Message); return false; }
        }
        private int SymbolIndex(string symbol)
        {
            var i = Array.FindIndex(_state.symbols, x => string.Equals(x, symbol, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
                throw new InvalidDataException("Unknown reel symbol.");
            return i;
        }
        private void Begin(string operation, long amount, string selection = null)
        {
            if (!(_laptopSession?.Ready ?? true) || _busy || _refreshing || GameActive || _pending != null || _state?.enabled != true || !CanUse || _restart || !_state.available)
                return;
            var request = new CasinoRequest { transactionId = Guid.NewGuid().ToString("N"), sequence = _state.sequence, operation = operation, economyVersion = 4, exchangeVersion = operation == "buy" || operation == "redeem" ? 3 : 0, amount = amount, currency = _currencies[_currency], machine = operation == "race" ? "boss-racing" : "rippers-slot", selection = selection };
            try
            {
                DurableRequestFile.WriteNew(_pendingPath, JsonConvert.SerializeObject(request));
                _pending = request;
                _pendingFromDisk = false;
                _requestSent = false;
                if (operation == "spin")
                {
                    BeginSpin(checked((int)amount));
                    Play(CasinoCue.Lever);
                }
                if (operation == "race")
                    BeginRace(checked((int)amount));
                StartCoroutine(Guard(Submit()));
            }
            catch (Exception ex) { StopAutoOnError(); _presentation.Reject(_state.balance); _racePresentation.Reject(_state.balance); _shownBalance = _state.balance; _casinoAudio.StopSpin(); _message = "Payment could not be started. Close and reopen the casino."; Logger.LogError("[RippersCasino] " + ex.Message); }
        }
        private void BeginSpin(int amount)
        {
            _spinClockRate = _slowReels.Value ? .2f : 1;
            _jackpotOutcome = _jackpotReveal = false;
            _presentation.Begin(_shownBalance, amount);
            _shownBalance = _presentation.VisibleBalance;
            _waitingAt = Time.unscaledTime;
            _lastStop = -1;
        }
        private IEnumerator Submit()
        {
            if (_pending == null || _busy || !CanUse || _restart)
                yield break;
            if (_requestSent && !_pendingFromDisk)
            {
                RequireRestart("Restart Tarkov before trying this payment again.");
                yield break;
            }
            _busy = true;
            _message = _pending.operation == "spin" ? "REELS RUNNING…" : _pending.operation == "race" ? "RACE IN PROGRESS…" : "PROCESSING…";
            if (_pending.operation == "race" && !_racePresentation.Active && _state.available && _shownBalance >= _pending.amount)
                BeginRace(checked((int)_pending.amount));
            if (_pending.operation == "spin" && !_presentation.Active && _state.available && _shownBalance >= _pending.amount)
            {
                BeginSpin(checked((int)_pending.amount));

            }
            var session = ItemUiContext.Instance.ClientSession;
            var flush = session.FlushOperationQueue();
            var end = Time.unscaledTime + 25;
            while (!flush.IsCompleted && Time.unscaledTime < end)
                yield return null;
            if (!flush.IsCompleted || flush.IsFaulted || flush.IsCanceled || flush.Result.Failed)
            {
                _busy = false;
                StopAutoOnError();
                _message = "Stash busy. No payment was sent. Choose Resume to try again.";
                _presentation.Reject(_state.balance);
                _racePresentation.Reject(_state.balance);
                _shownBalance = _state.balance;
                _casinoAudio.StopSpin();
                yield break;
            }
            if (!CanUse)
            {
                StopAutoOnError();
                _busy = false;
                _presentation.Reject(_state.balance);
                _racePresentation.Reject(_state.balance);
                _shownBalance = _state.balance;
                _casinoAudio.StopSpin();
                yield break;
            }
            var completed = new TaskCompletionSource<IResult>();
            _requestSent = true;
            // Action is transport routing, excluded from the persistent payload fingerprint.
            // Recovery retains the original machine, ID and sequence.
            _pending.Action = "RippersCasino";
            try
            {
                session.SendOperationRightNow(_pending, result => completed.TrySetResult(result));
            }
            catch (Exception ex) { completed.TrySetException(ex); }
            end = Time.unscaledTime + 30;
            while (!completed.Task.IsCompleted && Time.unscaledTime < end)
                yield return null;
            if (!completed.Task.IsCompleted || completed.Task.IsFaulted || completed.Task.IsCanceled || completed.Task.Result.Failed)
            {
                RequireRestart("Payment not confirmed. Restart Tarkov before trying again.");
                _busy = false;
                yield break;
            }
            var task = RequestHandler.GetJsonAsync("/ripperscasino/state");
            end = Time.unscaledTime + 25;
            while (!task.IsCompleted && Time.unscaledTime < end)
                yield return null;
            if (!task.IsCompleted || !ReadState(task))
            {
                RequireRestart("Payment not confirmed. Restart Tarkov to refresh your stash.");
                _busy = false;
                yield break;
            }
            var receipt = MatchingReceipt();
            if (receipt == null || receipt.syncRequired)
            {
                RequireRestart("Restart Tarkov to refresh your stash. Your payment record is saved.");
                _busy = false;
                yield break;
            }
            var wasSpin = _pending.operation == "spin";
            var wasRace = _pending.operation == "race";
            if (wasRace && string.IsNullOrEmpty(receipt.error) && !ValidRaceReceipt(receipt, _pending))
            {
                RequireRestart("Race result unavailable. Restart Tarkov to restore your game.");
                _busy = false;
                yield break;
            }
            if (wasSpin && string.IsNullOrEmpty(receipt.error) && (receipt.reels?.Length != 3 || receipt.reels.Any(x => !_state.symbols.Contains(x))))
            {
                RequireRestart("Spin result unavailable. Restart Tarkov to restore your game.");
                _busy = false;
                yield break;
            }
            if (!ClearPending())
            {
                RequireRestart("Your result is saved. Restart Tarkov before playing again.");
                _busy = false;
                yield break;
            }
            if (!_open)
            {
                _presentation.Reject(_state.balance);
                _racePresentation.Reject(_state.balance);
                _shownBalance = _state.balance;
                _lastRace = _state.lastRace;
                _busy = false;
                yield break;
            }
            if (!string.IsNullOrEmpty(receipt.error))
            {
                StopAutoOnError();
                _presentation.Reject(_state.balance);
                _racePresentation.Reject(_state.balance);
                _shownBalance = _state.balance;
                _casinoAudio.StopSpin();
                _message = PlayerMessage(receipt.error);
            }
            else if (wasSpin)
            {
                var targets = receipt.reels.Select(SymbolIndex).ToArray();
                if (!_presentation.Active)
                {
                    _presentation.Begin(checked(receipt.balance - receipt.payout + receipt.amount), checked((int)receipt.amount));
                    _shownBalance = _presentation.VisibleBalance;
                }
                _presentation.Resolve(targets, receipt.balance, receipt.payout, (Time.unscaledTime - _waitingAt) * _spinClockRate, _state.symbols.Length);
                _jackpotOutcome = JackpotPresentation.IsJackpot(targets, _state.triples, receipt.amount, receipt.payout);
                _resolveAt = Time.unscaledTime;
                Array.Copy(targets, _restSymbols, 3);
                _message = "REELS RUNNING…";
            }
            else if (wasRace)
                ResolveRace(receipt);
            else
            {
                _shownBalance = _state.balance;
                _message = (receipt.operation == "buy" ? "Deposited " : "Paid ") + CashNumber(receipt.currencyUnits) + " " + receipt.currency + ".";
                Play(CasinoCue.Exchange);
            }
            _busy = false;
        }
        private bool ClearPending()
        {
            try
            {
                File.Delete(_pendingPath);
                _pending = null;
                _pendingFromDisk = false;
                _requestSent = false;
                return true;
            }
            catch (Exception ex) { Logger.LogWarning("[RippersCasino] Recovery retained: " + ex.Message); return false; }
        }
        private void RequireRestart(string message)
        {
            StopAutoOnError();
            _restart = true;
            SetGuiVisible(true);
            _message = message;
            _casinoAudio?.StopSpin();
            _presentation.Reject(_shownBalance);
            _racePresentation.Reject(_shownBalance);
            if (!_open && CanUse)
                Open();
        }
    }
}
