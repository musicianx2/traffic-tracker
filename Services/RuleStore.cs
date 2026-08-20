using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrafficTracker.Models;

namespace TrafficTracker.Services;

/// <summary>
/// Engelleme kurallarini %APPDATA%\TrafficTracker\rules.json dosyasinda saklar.
/// </summary>
internal static class RuleStore
{
    private static readonly string DirPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficTracker");

    private static readonly string FilePath = Path.Combine(DirPath, "rules.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static List<BlockRule> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<BlockRule>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<BlockRule>>(json, Options) ?? new List<BlockRule>();
        }
        catch
        {
            return new List<BlockRule>();
        }
    }

    public static void Save(IEnumerable<BlockRule> rules)
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, Options));
        }
        catch
        {
            // Diske yazilamadi: sessizce yoksay (kural yine de bu oturumda calisir).
        }
    }
}
