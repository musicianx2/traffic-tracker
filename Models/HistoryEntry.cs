using System.IO;
using System.Text.Json.Serialization;

namespace TrafficTracker.Models;

public enum HistoryAction
{
    Added,
    Deleted,
    Enabled,
    Disabled
}

/// <summary>
/// Kullanicinin yaptigi bir kural islemi (zaman damgali). Geri alabilmek icin
/// kuralin tum alanlarinin anlik kopyasini tutar.
/// </summary>
public sealed class HistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public HistoryAction Action { get; set; }

    public string RuleId { get; set; } = string.Empty;
    public BlockKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public RuleProtocol Protocol { get; set; }
    public RuleDirection Direction { get; set; }
    public string Note { get; set; } = string.Empty;

    [JsonIgnore] public string TimeText => Timestamp.ToString("dd.MM.yyyy  HH:mm:ss");

    [JsonIgnore]
    public string ActionText => Action switch
    {
        HistoryAction.Added => "➕ Eklendi",
        HistoryAction.Deleted => "🗑 Silindi",
        HistoryAction.Enabled => "✅ Açıldı",
        HistoryAction.Disabled => "⏸ Kapatıldı",
        _ => "?"
    };

    [JsonIgnore]
    public string KindText => Kind switch
    {
        BlockKind.App => "Program",
        BlockKind.RemoteIp => "IP",
        BlockKind.RemotePort => "Port",
        _ => "?"
    };

    [JsonIgnore]
    public string DisplayTarget => Kind == BlockKind.App ? Path.GetFileName(Target) : Target;

    /// <summary>Bu gecmis kaydindan kurali yeniden olusturur (geri alma icin).</summary>
    public BlockRule ToRule() => new()
    {
        Id = RuleId,
        Kind = Kind,
        Target = Target,
        Protocol = Protocol,
        Direction = Direction,
        Note = Note,
        Enabled = true
    };

    public static HistoryEntry From(HistoryAction action, BlockRule rule) => new()
    {
        Action = action,
        RuleId = rule.Id,
        Kind = rule.Kind,
        Target = rule.Target,
        Protocol = rule.Protocol,
        Direction = rule.Direction,
        Note = rule.Note
    };
}
