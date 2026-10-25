using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace RippersCasino.Client
{
    public sealed partial class CasinoPlugin
    {
        private static readonly string[] TableGames = { "blackjack", "poker", "russian-roulette", "roulette" };
        private TableSnapshot _tables;
        private TableRequest _tablePending;
        private string _tablePendingPath, _tableCursor = "", _tableError, _tableActionError, _tableBuyIn = "100";
        private bool _tablePolling, _tableSending;
        private float _tablePollAt;
        private int _tableGame;
        private static int TablePage(string game) => game == "blackjack" ? 6 : game == "poker" ? 7 : game == "russian-roulette" ? 8 : 9;
        private void LoadTablePending()
        {
            _tablePendingPath = _pendingPath.Replace("pending-", "table-pending-");
            if (!File.Exists(_tablePendingPath))
                return;
            _tablePending = JsonConvert.DeserializeObject<TableRequest>(File.ReadAllText(_tablePendingPath));
            if (_tablePending == null || _tablePending.economyVersion != 4 || !Guid.TryParseExact(_tablePending.requestId, "N", out _) ||
                !new[] { "solo", "leave", "start", "hit", "stand", "double", "fold", "check", "call", "raise", "all-in", "bet", "take-turn" }.Contains(_tablePending.operation))
                throw new InvalidDataException("Invalid retained table action. Recovery file preserved.");
        }
        private void TickTableSession()
        {
            if (_readSession.CanPoll(HideoutScreenVisible) && (_page >= 5 || _tables?.table?.phase == "playing") && !_tablePolling && !_tableSending && !_busy && !_refreshing && !GameActive && Time.unscaledTime >= _tablePollAt)
                StartCoroutine(Guard(PollTableSession()));
        }
        private IEnumerator PollTableSession()
        {
            if (!_readSession.CanPoll(HideoutScreenVisible) || _tablePolling) yield break;
            var generation = _readSession.Generation;
            _tablePolling = true;
            try
            {
                var task = RequestHandler.PostJsonAsync("/ripperscasino/tables/state", JsonConvert.SerializeObject(new
                {
                    since = _tableCursor
                }));
                ObserveReadFailure(task);
                var deadline = Time.unscaledTime + 20;
                while (_readSession.Accept(generation) && !task.IsCompleted && Time.unscaledTime < deadline)
                    yield return null;
                if (!_readSession.Accept(generation)) yield break;
                if (!task.IsCompleted)
                    _tableError = "Table unavailable. Your bankroll is saved. Close and reopen to try again.";
                else
                {
                    try
                    {
                        ApplyTableResponse(JsonConvert.DeserializeObject<TableResponse>(task.GetAwaiter().GetResult()));
                    }
                    catch (Exception error) { _tableError = "Table unavailable. Close and reopen to try again."; Logger.LogWarning("[RippersCasino] Tables: " + error.Message); }
                }
            }
            finally { _tablePolling = false; _tablePollAt = _readSession.Accept(generation) ? Time.unscaledTime + (_tableError == null ? 1.5f : 5) : 0; }
        }
        private bool ApplyTableResponse(TableResponse response)
        {
            if (response == null)
                throw new InvalidDataException("Empty table response.");
            if (!string.IsNullOrEmpty(response.error))
            {
                _tableError = PlayerMessage(response.error);
                return false;
            }
            if (response.state != null)
            {
                if (response.state.economyVersion != 4 || response.state.balance < 0 || response.state.sequence < 1)
                    throw new InvalidDataException("Invalid table snapshot.");
                if (_open)
                {
                    ObserveBlackjack(_tables?.table?.blackjack, response.state.table?.blackjack);
                    ObservePoker(_tables?.table?.poker, response.state.table?.poker);
                    ObserveRoulette(_tables?.table?.roulette, response.state.table?.roulette, response.serverTime);
                    ObserveElimination(_tables?.table?.elimination, response.state.table?.elimination);
                }
                _tables = response.state;
                RefreshBlackjackBet();
                if (_state != null && !_busy && !GameActive && _pending == null)
                {
                    _state.balance = _shownBalance = _tables.balance;
                    _state.sequence = _tables.sequence;
                    _state.tableHeld = _tables.table?.seats?.Where(seat => seat.mine).Sum(seat => seat.stack) ?? 0;
                }
            }
            if (_tables == null)
                throw new InvalidDataException("A full table snapshot is required.");
            _tables.serverTime = response.serverTime;
            _tablesReceivedAt = Time.unscaledTime;
            _tableCursor = response.cursor ?? "";
            _tableError = null;
            return true;
        }
        private bool TableControls => _open && !_tableSending && !_busy && !_refreshing && !GameActive && _pending == null && _tablePending == null;
        private void BeginTableAction(string operation, int amount = 0, string selection = "")
        {
            if (!TableControls || _tables == null)
                return;
            var own = _tables.table;
            var request = new TableRequest
            {
                economyVersion = 4,
                requestId = Guid.NewGuid().ToString("N"),
                operation = operation,
                amount = amount,
                selection = selection,
                tableId = own?.id ?? "",
                version = own?.version ?? 0
            };
            if (operation == "solo")
            {
                if (!int.TryParse(_tableBuyIn, out request.amount) || request.amount < 1)
                    return;
                request.game = TableGames[_tableGame];
            }
            try
            {
                DurableRequestFile.WriteNew(_tablePendingPath, JsonConvert.SerializeObject(request));
                _tableActionError = null;
                _tablePending = request;
                StartCoroutine(Guard(SendTableAction()));
            }
            catch (Exception error) { _tableError = "Action could not be saved. Close and reopen before trying again."; Logger.LogWarning("[RippersCasino] Table recovery: " + error); }
        }
        private IEnumerator SendTableAction()
        {
            if (_tablePending == null || _tablePending.economyVersion != 4 || _tableSending || _busy || _pending != null || GameActive)
                yield break;
            _tableSending = true;
            var operation = _tablePending.operation;
            try
            {
                while (_tablePolling)
                    yield return null;
                var task = RequestHandler.PostJsonAsync("/ripperscasino/tables/action", JsonConvert.SerializeObject(_tablePending));
                var deadline = Time.unscaledTime + 25;
                while (!task.IsCompleted && Time.unscaledTime < deadline)
                    yield return null;
                if (!task.IsCompleted)
                {
                    _tableError = "Action not confirmed. Choose Resume to try again.";
                    yield break;
                }
                TableResponse response;
                try
                {
                    response = JsonConvert.DeserializeObject<TableResponse>(task.GetAwaiter().GetResult());
                }
                catch (Exception error) { _tableError = "Action not confirmed. Choose Resume to try again."; Logger.LogWarning("[RippersCasino] Table action: " + error.Message); yield break; }
                if (response == null)
                {
                    _tableError = "Action not confirmed. Choose Resume to try again.";
                    yield break;
                }
                var success = ApplyTableResponse(response);
                if (!success && !response.retryable) { _tableActionError = PlayerMessage(response.error); _tableError = null; }
                if (success || !response.retryable)
                {
                    try
                    {
                        File.Delete(_tablePendingPath);
                        _tablePending = null;
                    }
                    catch (Exception error) { _tableError = "Action saved. Choose Resume before playing again."; Logger.LogWarning("[RippersCasino] " + error.Message); }
                }
                if (success)
                {
                    Play(operation == "start" ? CasinoCue.ChipPlace : operation == "hit" || operation == "stand" || operation == "double" ? CasinoCue.Button : CasinoCue.Exchange);
                    _message = response.state?.replayed == true ? "Action restored." : operation == "leave" ? _tables?.table == null ? "Bankroll returned to your balance." : "Leaving after this round." : "";
                }
                _tableCursor = "";
            }
            finally { _tableSending = false; _tablePollAt = 0; }
        }
    }
}
