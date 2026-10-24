namespace RippersCasino.Core;

public sealed class RacerConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Weight { get; set; }
    // Total returned chips, including the stake. Never fractional profit odds.
    public int ReturnMultiplier { get; set; }
}
public sealed class RaceConfig
{
    public const string MachineId = "boss-racing";
    public bool Enabled { get; set; } = true;
    public RacerConfig[] Racers { get; set; } =
    [
        new() { Id="killa", Name="Killa", Weight=3465, ReturnMultiplier=2 },
        new() { Id="tagilla", Name="Tagilla", Weight=2310, ReturnMultiplier=3 },
        new() { Id="gluhar", Name="Gluhar", Weight=1386, ReturnMultiplier=5 },
        new() { Id="shturman", Name="Shturman", Weight=990, ReturnMultiplier=7 },
        new() { Id="sanitar", Name="Sanitar", Weight=770, ReturnMultiplier=9 },
        new() { Id="reshala", Name="Reshala", Weight=630, ReturnMultiplier=11 }
    ];
    public void Validate()
    {
        string[] ids = ["killa", "tagilla", "gluhar", "shturman", "sanitar", "reshala"];
        if (Racers is null || Racers.Length != 6 || Racers.Any(r => r is null) || !Racers.Select(r => r.Id).SequenceEqual(ids))
            throw new InvalidOperationException("Boss Racing requires the six documented racers in order.");
        if (Racers.Any(r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 24 || r.Weight < 1 || r.Weight > 100000 || r.ReturnMultiplier < 1 || r.ReturnMultiplier > 10000))
            throw new InvalidOperationException("Invalid Boss Racing weights, names or returns.");
        var total = Racers.Sum(r => r.Weight);
        if (Racers.Any(r => (long)r.Weight * r.ReturnMultiplier >= total))
            throw new InvalidOperationException("Every Boss Racing selection must have a theoretical return below 100%.");
    }
    public string Pick(int ticket)
    {
        if (ticket < 0 || ticket >= Racers.Sum(r => r.Weight))
            throw new ArgumentOutOfRangeException(nameof(ticket));
        foreach (var racer in Racers)
        {
            if (ticket < racer.Weight)
                return racer.Id;
            ticket -= racer.Weight;
        }
        throw new InvalidOperationException();
    }
    public string[] Finish(IRandomSource random)
    {
        var winner = Pick(random.Next(Racers.Sum(r => r.Weight)));
        var rest = Racers.Where(r => r.Id != winner).Select(r => r.Id).ToArray();
        for (var i = rest.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }
        return [winner, .. rest];
    }
}
