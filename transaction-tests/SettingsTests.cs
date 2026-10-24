using System.Reflection;
using System.Text.Json;
using RippersCasino.Core;
using RippersCasino.Server;

internal static partial class Program
{
    private static async Task SettingsTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "casino-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ripperscasino.json");
        var defaultsPath = Path.Combine(directory, "ripperscasino.defaults.json");
        var defaults = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(typeof(CasinoSettings).Assembly.Location)!, "ripperscasino.defaults.json"));
        var load = typeof(CasinoSettings).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
        CasinoConfig Load() => (CasinoConfig)load.Invoke(null, [directory])!;
        try
        {
            var preserved = new[] { "profile.json", "pending.json", "receipt.json" }.ToDictionary(name => name, name => "preserve-" + name);
            foreach (var file in preserved) File.WriteAllText(Path.Combine(directory, file.Key), file.Value);
            File.WriteAllBytes(defaultsPath, defaults);
            Check(Load().EconomyVersion == 4 && File.ReadAllBytes(path).SequenceEqual(defaults), "direct-extract first load creates the complete current configuration from defaults");
            var custom = Load(); custom.Enabled = false;
            File.WriteAllText(path, JsonSerializer.Serialize(custom, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var bytes = File.ReadAllBytes(path);
            File.WriteAllText(defaultsPath, "invalid replacement defaults");
            Check(!Load().Enabled && File.ReadAllBytes(path).SequenceEqual(bytes), "upgraded defaults never overwrite an existing configuration");
            File.Delete(path); File.WriteAllBytes(defaultsPath, defaults);
            Check(Load().EconomyVersion == 4, "missing configuration can be recreated independently of existing wallet/recovery data");
            foreach (var file in preserved) Check(File.ReadAllText(Path.Combine(directory, file.Key)) == file.Value, "configuration creation preserves " + file.Key);
            using var defaultsDocument = JsonDocument.Parse(defaults);
            var raw = defaultsDocument.RootElement.GetRawText();
            var old = raw.Replace("\"economyVersion\": 4", "\"economyVersion\": 3");
            Check(old != raw, "fixture has an explicitly incompatible configuration version");
            File.WriteAllText(path, old); bytes = File.ReadAllBytes(path);
            await ThrowsAsync<TargetInvocationException>(() => Task.Run(() => Load()), "incompatible existing configuration fails without conversion or reset");
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "incompatible configuration remains byte-for-byte intact");
            File.Delete(path); File.WriteAllText(defaultsPath, "invalid");
            await ThrowsAsync<TargetInvocationException>(() => Task.Run(() => Load()), "invalid defaults fail before creating a live configuration");
            Check(!File.Exists(path) && !Directory.EnumerateFiles(directory, "*.tmp").Any(), "failed configuration initialization leaves no partial configuration or temporary file");
            File.WriteAllBytes(defaultsPath, defaults);
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Load())));
            Check(File.ReadAllBytes(path).SequenceEqual(defaults), "concurrent startup creates exactly one complete configuration without overwrite");
        }
        finally { Directory.Delete(directory, true); }
        var metadata = new ModMetadata();
        Check(metadata.ModGuid == "com.itsripper.ripperscasino" && metadata.Name == "RippersCasino" && metadata.Author == "itsRipper" && metadata.Version.ToString() == "1.0.0", "SPT metadata retains the stable client/server GUID and compliant name, author and version");
    }
}
