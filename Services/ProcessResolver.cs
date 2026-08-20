using System.Diagnostics;

namespace TrafficTracker.Services;

/// <summary>
/// Bir refresh anindaki tum process'lerin PID -> ad haritasini cikarir.
/// Baglanti basina tek tek Process.GetProcessById cagirmaktan cok daha ucuz
/// ve erisilemeyen process'lerde istisna uretmez.
/// </summary>
internal static class ProcessResolver
{
    public static Dictionary<int, string> Snapshot()
    {
        var map = new Dictionary<int, string>(256)
        {
            [0] = "System Idle",
            [4] = "System"
        };

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                map[p.Id] = p.ProcessName;
            }
            catch
            {
                // Erisim reddi / process kapanmis: yoksay.
            }
            finally
            {
                p.Dispose();
            }
        }

        return map;
    }
}
