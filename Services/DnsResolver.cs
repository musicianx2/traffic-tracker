using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TrafficTracker.Services;

/// <summary>
/// Uzak IP adresleri icin arka planda ters DNS cozumlemesi yapar ve sonuclari
/// onbellekler. Ozel/loopback adresler cozulmeye calisilmaz.
/// GetHost cagrisi asla bloklamaz: sonuc hazir degilse null doner, hazir olunca
/// bir sonraki refresh'te gelir.
/// </summary>
public sealed class DnsResolver
{
    // deger: null = cozumleniyor, "" = sonuc yok/hata, dolu = host adi
    private readonly ConcurrentDictionary<string, string?> _cache = new();

    public string? GetHost(IPAddress? ip)
    {
        if (ip is null) return null;
        if (IPAddress.IsLoopback(ip)) return null;
        if (IsUninteresting(ip)) return null;

        string key = ip.ToString();

        if (_cache.TryGetValue(key, out var cached))
            return cached; // null (cozumleniyor), "" (yok) veya host adi

        // Ilk kez goruluyor: yer tut ve arka planda coz.
        if (!_cache.TryAdd(key, null))
            return _cache.TryGetValue(key, out var v) ? v : null;

        _ = Task.Run(async () =>
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip).ConfigureAwait(false);
                _cache[key] = string.IsNullOrWhiteSpace(entry.HostName) ? "" : entry.HostName;
            }
            catch
            {
                _cache[key] = "";
            }
        });

        return null;
    }

    private static bool IsUninteresting(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            if (b[0] == 10) return true;                          // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;          // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;          // 169.254.0.0/16 (APIPA)
            if (b[0] == 0) return true;                           // 0.0.0.0
            if (b[0] == 127) return true;                         // loopback
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;
            if (ip.IsIPv6SiteLocal) return true;
            byte[] b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;               // fc00::/7 (ULA)
            if (IPAddress.IPv6Any.Equals(ip)) return true;
        }
        return false;
    }
}
