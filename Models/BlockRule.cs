using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TrafficTracker.Models;

public enum BlockKind
{
    App,        // program (exe yolu) bazli
    RemoteIp,   // uzak IP bazli
    RemotePort  // uzak port bazli
}

public enum RuleProtocol
{
    Any,
    Tcp,
    Udp
}

public enum RuleDirection
{
    Outbound,
    Inbound,
    Both
}

/// <summary>
/// Kullanicinin tanimladigi bir engelleme kurali. UI'da listelenir, JSON'a
/// kaydedilir ve Windows Guvenlik Duvari'na yansitilir (FirewallManager).
/// </summary>
public sealed class BlockRule : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public BlockKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public RuleProtocol Protocol { get; set; } = RuleProtocol.Any;
    public RuleDirection Direction { get; set; } = RuleDirection.Both;
    public string Note { get; set; } = string.Empty;

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    /// <summary>Guvenlik duvari kural adlarinin ortak on eki.</summary>
    [JsonIgnore] public string FirewallBaseName => $"TrafficTracker: {Id}";

    [JsonIgnore]
    public string KindText => Kind switch
    {
        BlockKind.App => "Program",
        BlockKind.RemoteIp => "IP",
        BlockKind.RemotePort => "Port",
        _ => "?"
    };

    [JsonIgnore]
    public string DisplayTarget => Kind == BlockKind.App
        ? Path.GetFileName(Target)
        : Target;

    [JsonIgnore]
    public string ProtocolText => Protocol switch
    {
        RuleProtocol.Tcp => "TCP",
        RuleProtocol.Udp => "UDP",
        _ => "Tümü"
    };

    [JsonIgnore]
    public string DirectionText => Direction switch
    {
        RuleDirection.Outbound => "Giden",
        RuleDirection.Inbound => "Gelen",
        _ => "Çift yön"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
