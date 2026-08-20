using System.Net;
using System.Runtime.InteropServices;
using TrafficTracker.Models;

namespace TrafficTracker.Native;

/// <summary>
/// iphlpapi.dll uzerinden acik TCP/UDP baglantilarini sahip process (PID) ile
/// birlikte okur. IPv4 ve IPv6 desteklenir. Yonetici gerektirmez.
/// </summary>
internal static class IpHelper
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;

    // TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    // UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID
    private const int UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow4
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow4
    {
        public uint localAddr;
        public uint localPort;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        public uint pid;
    }

    public static List<ConnectionInfo> GetAllConnections()
    {
        var list = new List<ConnectionInfo>(512);
        ReadTcp(AF_INET, list);
        ReadTcp(AF_INET6, list);
        ReadUdp(AF_INET, list);
        ReadUdp(AF_INET6, list);
        return list;
    }

    // Port alani DWORD icinde network byte order'da saklanir (dusuk 2 bayt).
    private static int ToPort(uint raw) => ((int)(raw & 0xFF) << 8) | (int)((raw >> 8) & 0xFF);

    private static void ReadTcp(int af, List<ConnectionInfo> list)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return;

            int count = Marshal.ReadInt32(buf);
            IntPtr ptr = buf + 4;

            if (af == AF_INET)
            {
                int stride = Marshal.SizeOf<TcpRow4>();
                for (int i = 0; i < count; i++)
                {
                    var r = Marshal.PtrToStructure<TcpRow4>(ptr);
                    list.Add(new ConnectionInfo
                    {
                        Protocol = Protocol.Tcp,
                        LocalAddress = new IPAddress(BitConverter.GetBytes(r.localAddr)),
                        LocalPort = ToPort(r.localPort),
                        RemoteAddress = new IPAddress(BitConverter.GetBytes(r.remoteAddr)),
                        RemotePort = ToPort(r.remotePort),
                        State = TcpState(r.state),
                        Pid = (int)r.pid
                    });
                    ptr += stride;
                }
            }
            else
            {
                int stride = Marshal.SizeOf<TcpRow6>();
                for (int i = 0; i < count; i++)
                {
                    var r = Marshal.PtrToStructure<TcpRow6>(ptr);
                    list.Add(new ConnectionInfo
                    {
                        Protocol = Protocol.Tcp,
                        LocalAddress = new IPAddress(r.localAddr),
                        LocalPort = ToPort(r.localPort),
                        RemoteAddress = new IPAddress(r.remoteAddr),
                        RemotePort = ToPort(r.remotePort),
                        State = TcpState(r.state),
                        Pid = (int)r.pid
                    });
                    ptr += stride;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void ReadUdp(int af, List<ConnectionInfo> list)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) return;

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buf, ref size, false, af, UDP_TABLE_OWNER_PID, 0) != 0)
                return;

            int count = Marshal.ReadInt32(buf);
            IntPtr ptr = buf + 4;

            if (af == AF_INET)
            {
                int stride = Marshal.SizeOf<UdpRow4>();
                for (int i = 0; i < count; i++)
                {
                    var r = Marshal.PtrToStructure<UdpRow4>(ptr);
                    list.Add(new ConnectionInfo
                    {
                        Protocol = Protocol.Udp,
                        LocalAddress = new IPAddress(BitConverter.GetBytes(r.localAddr)),
                        LocalPort = ToPort(r.localPort),
                        RemoteAddress = null,
                        RemotePort = 0,
                        State = string.Empty,
                        Pid = (int)r.pid
                    });
                    ptr += stride;
                }
            }
            else
            {
                int stride = Marshal.SizeOf<UdpRow6>();
                for (int i = 0; i < count; i++)
                {
                    var r = Marshal.PtrToStructure<UdpRow6>(ptr);
                    list.Add(new ConnectionInfo
                    {
                        Protocol = Protocol.Udp,
                        LocalAddress = new IPAddress(r.localAddr),
                        LocalPort = ToPort(r.localPort),
                        RemoteAddress = null,
                        RemotePort = 0,
                        State = string.Empty,
                        Pid = (int)r.pid
                    });
                    ptr += stride;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string TcpState(uint s) => s switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN-SENT",
        4 => "SYN-RCVD",
        5 => "ESTABLISHED",
        6 => "FIN-WAIT1",
        7 => "FIN-WAIT2",
        8 => "CLOSE-WAIT",
        9 => "CLOSING",
        10 => "LAST-ACK",
        11 => "TIME-WAIT",
        12 => "DELETE-TCB",
        _ => "?"
    };
}
