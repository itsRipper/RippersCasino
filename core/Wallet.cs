namespace RippersCasino.Core;

public sealed record CasinoCommand(string TransactionId, long Sequence, string Operation, string Currency, long Amount, string Machine, string Selection = "", int ExchangeVersion = 0, int EconomyVersion = 4)
{
    public string Fingerprint => $"economy:{EconomyVersion}:{Sequence}:{Operation}:{Currency}:{Amount}:{Machine}" + (Operation == "race" ? $":{Selection}" : "") + (ExchangeVersion == 0 ? "" : $":cashier:{ExchangeVersion}");
}
public sealed class Receipt
{
    public string Id { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Currency { get; set; } = "";
    public long CurrencyUnits { get; set; }
    public int RemainderRub { get; set; }
    public long ChipDelta { get; set; }
    public long Amount { get; set; }
    public string[] Reels { get; set; } = [];
    public string Selection { get; set; } = "";
    public string Winner { get; set; } = "";
    public string[] FinishOrder { get; set; } = [];
    public long Payout { get; set; }
    public long Balance { get; set; }
    public string? Error { get; set; }
}
public sealed class Wallet
{
    public int Version { get; set; } = 4;
    public int RemainderRub { get; set; }
    public long Chips { get; set; }
    public long NextSequence { get; set; } = 1;
    public string LastRequestId { get; set; } = "";
    public Receipt? Last { get; set; }
    public Receipt? LastSlot { get; set; }
    public Receipt? LastRace { get; set; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long TableEscrow
    {
        get
        {
            if (AdditionalData?.TryGetValue("CasinoTableReceipt", out var receipt) != true)
                return 0;
            if (receipt.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !receipt.TryGetProperty("Escrow", out var amount) || !amount.TryGetInt64(out var held) || held < 0)
                throw new InvalidOperationException("Your saved bankroll could not be restored. Close the casino and check the log.");
            return held;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public long TableLiability
    {
        get
        {
            if (AdditionalData?.TryGetValue("CasinoTableReceipt", out var receipt) != true)
                return 0;
            if (receipt.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidOperationException("Your saved bankroll could not be restored. Close the casino and check the log.");
            if (!receipt.TryGetProperty("Liability", out var amount) || !amount.TryGetInt64(out var held) || held < 0)
                throw new InvalidOperationException("Your saved bankroll could not be restored. Close the casino and check the log.");
            return held;
        }
    }
    public void Validate()
    {
        if (Version != 4 || NextSequence < 1 || Chips < 0 || Chips > CashierTerms.MaximumChips || RemainderRub is < 0 or >= CashierTerms.ChipRubles)
            throw new InvalidOperationException("Your casino save needs the current installation. Close the casino and check the log.");
        var capacity = CashierTerms.RemainingCapacity(CashierTerms.MaximumChips, Chips, TableEscrow, TableLiability);
        if (capacity == 0 && (TableEscrow > CashierTerms.MaximumChips - Chips || TableLiability > CashierTerms.MaximumChips - Chips - TableEscrow))
            throw new InvalidOperationException("Your saved bankroll could not be restored. Close the casino and check the log.");
    }
    public bool IsReplay(CasinoCommand request)
    {
        if (!Guid.TryParseExact(request.TransactionId, "N", out _))
            throw new ArgumentException("This action could not be submitted. Close and reopen the casino.");
        if (request.Sequence == NextSequence - 1 && request.TransactionId == LastRequestId)
        {
            if (Last?.Fingerprint != request.Fingerprint)
                throw new ArgumentException("This action was already submitted. Close and reopen the casino.");
            return true;
        }
        if (request.Sequence != NextSequence || request.TransactionId == LastRequestId)
            throw new ArgumentException("This action is no longer current. Close and reopen the casino.");
        return false;
    }
}
