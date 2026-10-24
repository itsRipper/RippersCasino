namespace RippersCasino.Core;

public sealed class CasinoConfig
{
    public int EconomyVersion { get; set; } = 4;
    public CashierTerms Cashier { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool RedemptionEnabled { get; set; } = true;
    public long MaxBalance { get; set; } = CashierTerms.MaximumChips;
    public MachineConfig Machine { get; set; } = new();
    public RaceConfig Racing { get; set; } = new();
    public Tables.TableOptions Tables { get; set; } = new();
    public void Validate()
    {
        Cashier.Validate();
        if (EconomyVersion != 4 || MaxBalance < 1 || MaxBalance > CashierTerms.MaximumChips) throw new InvalidOperationException("Invalid wallet capacity.");
        Tables.Validate();
        if (Tables.MaxBuyIn > MaxBalance)
            throw new InvalidOperationException("Table buy-in exceeds wallet capacity.");
        Machine.Validate();
        Racing.Validate();
        if ((long)Machine.Bets.Max() * Racing.Racers.Max(r => r.ReturnMultiplier) > MaxBalance)
            throw new InvalidOperationException("Maximum racing payout exceeds chip limit.");
        if ((long)Machine.Bets.Max() * Math.Max(Machine.Triples.Max(), Machine.TwoCherries) > MaxBalance)
            throw new InvalidOperationException("Maximum jackpot exceeds chip limit.");
    }
}
public sealed class MachineConfig
{
    public string Id { get; set; } = "rippers-slot";
    public int[] Bets { get; set; } = [1, 5, 10, 25, 100, 500];
    public string[] Symbols { get; set; } = ["7", "DIAMOND", "BAR", "BELL", "CHERRY", "LEMON"];
    public int[] Weights { get; set; } = [1, 2, 3, 4, 10, 10];
    public int[] Triples { get; set; } = [100, 50, 25, 15, 10, 2];
    public int TwoCherries { get; set; } = 2;

    public void Validate()
    {
        if (Id != "rippers-slot" || Symbols.Length != 6 || !Symbols.SequenceEqual(new[] { "7", "DIAMOND", "BAR", "BELL", "CHERRY", "LEMON" }))
            throw new InvalidOperationException("This machine requires the six documented symbols and id rippers-slot.");
        if (Weights.Length != 6 || Triples.Length != 6 || Weights.Any(w => w < 1 || w > 10000) || Triples.Any(p => p < 0 || p > 10000) || TwoCherries < 0 || TwoCherries > 10000)
            throw new InvalidOperationException("Invalid reel weights or multipliers.");
        if (Bets.Length is < 1 or > 12 || Bets.Distinct().Count() != Bets.Length || Bets.Any(b => b < 1 || b > 10000))
            throw new InvalidOperationException("Bets must be distinct positive integers up to 10000.");
        if (new SlotRules(this).Statistics().Rtp >= 1)
            throw new InvalidOperationException("Configured machine must have RTP below 100%.");
    }
}
