using System.Runtime.InteropServices;
using System.Text;

namespace TrafficTracker.Native;

/// <summary>
/// PID'den process'in tam exe yolunu cozer (program bazli engelleme icin).
/// QueryFullProcessImageName, Process.MainModule'e gore daha az izin ister ve
/// 32/64-bit uyumsuzlugunda istisna atmaz.
/// </summary>
internal static class ProcessPath
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder buffer, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static string? Get(int pid)
    {
        if (pid <= 4) return null; // System / Idle

        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;

        try
        {
            int capacity = 1024;
            var sb = new StringBuilder(capacity);
            return QueryFullProcessImageName(h, 0, sb, ref capacity) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
