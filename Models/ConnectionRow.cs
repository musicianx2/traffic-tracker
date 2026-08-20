using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace TrafficTracker.Models;

/// <summary>
/// Arayuzde gosterilen satir. Degeri zamanla degisen alanlar (State, Process, Host)
/// INotifyPropertyChanged ile canli guncellenir.
/// </summary>
public sealed class ConnectionRow : INotifyPropertyChanged
{
    public required string Key { get; init; }

    public required string Protocol { get; init; }
    public required string LocalEndpoint { get; init; }
    public required string RemoteAddress { get; init; }
    public required int RemotePort { get; init; }
    public required int Pid { get; init; }

    private string _process = string.Empty;
    public string Process
    {
        get => _process;
        set => Set(ref _process, value);
    }

    private string _host = string.Empty;
    public string Host
    {
        get => _host;
        set => Set(ref _host, value);
    }

    private string _state = string.Empty;
    public string State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    /// <summary>Son gorulme zamani damgasi (kaybolan satirlari temizlemek icin).</summary>
    public long LastSeenTick { get; set; }

    // --- Program (PID) bazli anlik bant genisligi ---

    private double _downRate;
    public double DownRate
    {
        get => _downRate;
        set { if (_downRate == value) return; _downRate = value; Raise(nameof(DownRate)); Raise(nameof(DownRateText)); }
    }

    private double _upRate;
    public double UpRate
    {
        get => _upRate;
        set { if (_upRate == value) return; _upRate = value; Raise(nameof(UpRate)); Raise(nameof(UpRateText)); }
    }

    private bool _overThreshold;
    public bool OverThreshold
    {
        get => _overThreshold;
        set { if (_overThreshold == value) return; _overThreshold = value; Raise(nameof(OverThreshold)); }
    }

    public string DownRateText => FormatRate(_downRate);
    public string UpRateText => FormatRate(_upRate);

    private static string FormatRate(double bps)
    {
        if (bps < 1) return "";
        if (bps < 1024) return $"{bps:0} B/s";
        if (bps < 1024 * 1024) return $"{bps / 1024:0.0} KB/s";
        return $"{bps / (1024 * 1024):0.00} MB/s";
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Uzak IP + (varsa) cozumlenen host adi tek kolonda.</summary>
    public string RemoteDisplay =>
        string.IsNullOrEmpty(Host) || Host == RemoteAddress
            ? RemoteAddress
            : $"{RemoteAddress}  ({Host})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Host))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoteDisplay)));
    }
}
