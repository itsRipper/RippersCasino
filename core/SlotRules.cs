using System.Security.Cryptography;

namespace RippersCasino.Core;

public interface IRandomSource
{
    int Next(int exclusiveMax);
}
public sealed class SecureRandom : IRandomSource
{
    public int Next(int exclusiveMax) => RandomNumberGenerator.GetInt32(exclusiveMax);
}

public sealed class SlotRules(MachineConfig machine)
{
    public string[] Spin(IRandomSource random) => Enumerable.Range(0, 3).Select(_ => Pick(random.Next(machine.Weights.Sum()))).ToArray();
    public string Pick(int value)
    {
        if (value < 0 || value >= machine.Weights.Sum())
            throw new ArgumentOutOfRangeException(nameof(value));
        for (var i = 0; i < machine.Weights.Length; i++)
        {
            if (value < machine.Weights[i])
                return machine.Symbols[i];
            value -= machine.Weights[i];
        }
        throw new InvalidOperationException();
    }
    public int Multiplier(IReadOnlyList<string> reels)
    {
        if (reels.Count != 3 || reels.Any(s => !machine.Symbols.Contains(s)))
            throw new ArgumentException("Invalid reel outcome.");
        if (reels.All(s => s == reels[0]))
            return machine.Triples[Array.IndexOf(machine.Symbols, reels[0])];
        return reels.Count(s => s == "CHERRY") == 2 ? machine.TwoCherries : 0;
    }
    public (double Rtp, double HitFrequency) Statistics()
    {
        double paid = 0, hits = 0, total = Math.Pow(machine.Weights.Sum(), 3);
        for (int a = 0; a < 6; a++)
            for (int b = 0; b < 6; b++)
                for (int c = 0; c < 6; c++)
                {
                    var weight = (double)machine.Weights[a] * machine.Weights[b] * machine.Weights[c];
                    var multiplier = Multiplier([machine.Symbols[a], machine.Symbols[b], machine.Symbols[c]]);
                    paid += weight * multiplier;
                    if (multiplier > 0)
                        hits += weight;
                }
        return (paid / total, hits / total);
    }
}

