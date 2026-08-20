using System.Net.NetworkInformation;

namespace TrafficTracker.Services;

/// <summary>
/// Tum aktif ag arabirimlerinin toplam alinan/gonderilen bayt sayacini okur.
/// Iki okuma arasindaki fark ile anlik hiz (bayt/sn) hesaplanir.
/// </summary>
internal static class NetSpeedService
{
    public static (long received, long sent) TotalBytes()
    {
        long recv = 0, sent = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            try
            {
                var st = ni.GetIPStatistics();
                recv += st.BytesReceived;
                sent += st.BytesSent;
            }
            catch
            {
                // bazi sanal arabirimler istatistik vermez: yoksay
            }
        }
        return (recv, sent);
    }

    public static string Format(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:0.0} KB/s";
        return $"{bytesPerSec / (1024 * 1024):0.00} MB/s";
    }
}
