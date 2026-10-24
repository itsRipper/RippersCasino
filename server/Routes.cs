using RippersCasino.Core.Tables;
using RippersCasino.Server.Tables;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Utils;

namespace RippersCasino.Server;

public record CasinoRequest : BaseInteractionRequestData
{
    [JsonPropertyName("transactionId")] public string TransactionId { get; set; } = "";
    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }
    [JsonPropertyName("operation")] public string Operation { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("amount")]
    public long Amount { get; set; }
    [JsonPropertyName("machine")] public string Machine { get; set; } = "";
    [JsonPropertyName("economyVersion")] public int EconomyVersion { get; set; }
    [JsonPropertyName("exchangeVersion")] public int ExchangeVersion { get; set; }
    [JsonPropertyName("selection")] public string Selection { get; set; } = "";
}

[Injectable]
public sealed class CasinoItemRouter(CasinoService service) : ItemEventRouter([
    new ItemRouteAction<CasinoRequest>("RippersCasino", (url, pmc, body, session, output, token) => service.Execute(session, body, output))
]);

[Injectable]
public sealed class CasinoStateRouter(JsonUtil json, CasinoService service) : StaticRouter(json, [
    new RouteAction<EmptyRequestData>("/ripperscasino/state", (url, body, session, output, token) => service.State(session))
]);

[Injectable]
public sealed class CasinoTableRouter(JsonUtil json, CasinoTableService tables) : StaticRouter(json, [
    new RouteAction<TablePollRequest>("/ripperscasino/tables/state", (url, body, session, output, token) => tables.Poll(session, body?.Since ?? "")),
    new RouteAction<TableActionRequest>("/ripperscasino/tables/action", (url, body, session, output, token) => tables.Act(session, new(body.RequestId, body.Operation, body.TableId, body.Version, body.Game, body.Amount, body.Selection), body.EconomyVersion))
]);

public record TableActionRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("tableId")] string TableId = "",
    [property: JsonPropertyName("version")] long Version = 0,
    [property: JsonPropertyName("game")] string Game = "",
    [property: JsonPropertyName("amount")] int Amount = 0,
    [property: JsonPropertyName("selection")] string Selection = "",
    [property: JsonPropertyName("economyVersion")] int EconomyVersion = 0) : SPTarkov.Server.Core.Models.Utils.IRequestData;

public record TablePollRequest([property: JsonPropertyName("since")] string Since = "") : SPTarkov.Server.Core.Models.Utils.IRequestData;
