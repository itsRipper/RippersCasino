using System.Reflection;
using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Server.Transactions;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Servers;
using Path = System.IO.Path;

namespace RippersCasino.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.itsripper.ripperscasino";
    public string Name { get; init; } = "RippersCasino";
    public string Author { get; init; } = "itsRipper";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);
    public SemanticVersioning.Range SptVersion { get; init; } = new("=4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/itsRipper/RippersCasino";
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}

[Injectable(InjectionType.Singleton)]
public sealed class CasinoSettings
{
    public CasinoConfig Config { get; }
    public (double Rtp, double HitFrequency) SlotStatistics { get; }
    public CasinoSettings()
    {
        Config = Load(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        SlotStatistics = new SlotRules(Config.Machine).Statistics();
    }
    internal static CasinoConfig Load(string directory)
    {
        var path = Path.Combine(directory, "ripperscasino.json");
        if (!File.Exists(path))
        {
            var defaults = File.ReadAllBytes(Path.Combine(directory, "ripperscasino.defaults.json"));
            Parse(defaults);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(defaults);
                    file.Flush(true);
                }
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally { File.Delete(temporary); }
        }
        return Parse(File.ReadAllBytes(path));
    }
    private static CasinoConfig Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("economyVersion", out var economy) || economy.GetInt32() != 4)
            throw new InvalidOperationException("Incompatible casino configuration. Preserve profiles and recovery records; restore a matching configuration or use an explicitly reviewed conversion.");
        var config = JsonSerializer.Deserialize<CasinoConfig>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("RippersCasino configuration is empty.");
        config.Validate();
        return config;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public sealed class Bootstrap(SaveServer saves) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        CasinoPersistence.Current = new CasinoPersistence(saves);
        ProfileGate.Patch(new Harmony("com.itsripper.ripperscasino.transactions"));
        return Task.CompletedTask;
    }
}
