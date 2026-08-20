namespace TrafficTracker.Models;

/// <summary>
/// Bir Windows Update servisinin anlik durumu (arayuzde listelenir).
/// </summary>
public sealed class UpdateServiceState
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int StartValue { get; set; } = -1; // 2=Otomatik 3=Manuel 4=Devre disi
    public bool Running { get; set; }
    public bool Accessible { get; set; } = true;

    public string StartText => StartValue switch
    {
        2 => "Otomatik",
        3 => "Manuel",
        4 => "Devre dışı",
        _ => "Bilinmiyor"
    };

    public string StatusText => !Accessible ? "Erişilemiyor" : (Running ? "Çalışıyor" : "Durdu");

    /// <summary>Kilit acisindan "iyi" durum: devre disi ve calismyor.</summary>
    public bool IsLocked => StartValue == 4 && !Running;
}
