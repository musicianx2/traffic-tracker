using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TrafficTracker.Models;

/// <summary>
/// Bir programa (exe yolu) uygulanan hiz siniri. 0 = o yonde sinir yok.
/// </summary>
public sealed class ThrottleRule : INotifyPropertyChanged
{
    public string Exe { get; set; } = string.Empty;        // tam yol (kucuk harfle eslesir)
    public string DisplayName { get; set; } = string.Empty;

    private double _down;
    public double DownKBps { get => _down; set { if (_down == value) return; _down = value; Raise(nameof(DownKBps)); } }

    private double _up;
    public double UpKBps { get => _up; set { if (_up == value) return; _up = value; Raise(nameof(UpKBps)); } }

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; Raise(nameof(Enabled)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
