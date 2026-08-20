using System.IO;
using System.Text.Json;
using TrafficTracker.Models;

namespace TrafficTracker.Services;

internal static class ThrottleStore
{
    private static readonly string DirPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficTracker");

    private static readonly string FilePath = Path.Combine(DirPath, "throttle.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<ThrottleRule> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ThrottleRule>();
            return JsonSerializer.Deserialize<List<ThrottleRule>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch { return new List<ThrottleRule>(); }
    }

    public static void Save(IEnumerable<ThrottleRule> rules)
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, Options));
        }
        catch { /* yoksay */ }
    }
}
