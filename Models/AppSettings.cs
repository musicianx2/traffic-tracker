namespace TrafficTracker.Models;

/// <summary>
/// Kullanici ayarlari (esik vb.). settings.json'da saklanir.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Bir program bu esigi (KB/sn) asarsa satir renklendirilir.</summary>
    public double ThresholdKBps { get; set; } = 500;

    /// <summary>Esik uzeri satirlari vurgula.</summary>
    public bool HighlightEnabled { get; set; } = true;

    /// <summary>Program hiz sinirlama (WinDivert) etkin mi.</summary>
    public bool ThrottleEnabled { get; set; }
}
