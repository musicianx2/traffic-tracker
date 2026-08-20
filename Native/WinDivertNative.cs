using System.Runtime.InteropServices;

namespace TrafficTracker.Native;

/// <summary>
/// WinDivert 2.2 P/Invoke sarmalayicisi (yalnizca ihtiyac duyulan cagirlar).
/// WINDIVERT_ADDRESS 80 bayttir; yalnizca gerekli alanlar offset ile okunur.
/// </summary>
internal static class WinDivertNative
{
    public const int LayerNetwork = 0;
    public const int AddrSize = 80;
    public static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("WinDivert.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    public static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen, out uint recvLen, byte[] pAddr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    public static extern bool WinDivertSend(IntPtr handle, byte[] pPacket, uint packetLen, out uint sendLen, byte[] pAddr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    public static extern bool WinDivertClose(IntPtr handle);

    /// <summary>Adres bit alanindaki Outbound bayragi (Layer:8, Event:8, Sniffed:16, Outbound:17).</summary>
    public static bool IsOutbound(byte[] addr)
    {
        uint flags = BitConverter.ToUInt32(addr, 8);
        return ((flags >> 17) & 1) != 0;
    }
}
