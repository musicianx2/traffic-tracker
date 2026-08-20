using System.Net;

namespace TrafficTracker.Models;

public enum Protocol
{
    Tcp,
    Udp
}

/// <summary>
/// Isletim sisteminden okunan ham baglanti kaydi (bir refresh anindaki durum).
/// </summary>
public sealed class ConnectionInfo
{
    public Protocol Protocol { get; init; }

    public IPAddress LocalAddress { get; init; } = IPAddress.Any;
    public int LocalPort { get; init; }

    /// <summary>UDP dinleme kayitlarinda uzak uc yoktur -> null.</summary>
    public IPAddress? RemoteAddress { get; init; }
    public int RemotePort { get; init; }

    /// <summary>TCP durumu (ESTABLISHED, LISTEN, ...). UDP icin bos.</summary>
    public string State { get; init; } = string.Empty;

    public int Pid { get; init; }

    /// <summary>Refresh'ler arasi ayni baglantiyi eslestirmek icin benzersiz anahtar.</summary>
    public string Key =>
        $"{Protocol}|{LocalAddress}:{LocalPort}|{RemoteAddress}:{RemotePort}|{Pid}";
}
