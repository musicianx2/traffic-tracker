using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrafficTracker.Models;

namespace TrafficTracker.Services;

/// <summary>
/// Islem gecmisini %APPDATA%\TrafficTracker\history.json dosyasinda saklar.
/// </summary>
internal static class HistoryStore
{
    private const int MaxEntries = 1000;

    private static readonly string DirPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrafficTracker");

    private static readonly string FilePath = Path.Combine(DirPath, "history.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static List<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<HistoryEntry>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, Options) ?? new List<HistoryEntry>();
        }
        catch
        {
            return new List<HistoryEntry>();
        }
    }

    public static void Save(IEnumerable<HistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            var list = entries.Take(MaxEntries);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Options));
        }
        catch
        {
            // yoksay
        }
    }
}
