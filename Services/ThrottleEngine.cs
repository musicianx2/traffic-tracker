using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using TrafficTracker.Models;
using TrafficTracker.Native;

namespace TrafficTracker.Services;

/// <summary>
/// WinDivert ile program-basina hiz sinirlama (gelen+giden). GUVENLIK:
/// - Yalnizca aktif limit varken WinDivert handle'i acilir.
/// - Yalnizca limitli programin uc-noktalarina ait paketler zamanlanir; gerisi
///   aninda gecirilir.
/// - Hata / durdurma / kapanmada handle kapatilir -> ag aninda normale doner.
/// - Kuyruk asilirsa (asiri yuk) paketler gecirilir (agi tikamaz).
/// </summary>
internal sealed class ThrottleEngine : IDisposable
{
    private IntPtr _handle = WinDivertNative.InvalidHandle;
    private Thread? _recvThread, _workerThread;
    private volatile bool _running;

    public bool Running => _running;
    public string? Error { get; private set; }

    // exe (kucuk harf tam yol) -> bayt/sn (0 = o yonde sinir yok)
    private readonly ConcurrentDictionary<string, long> _down = new();
    private readonly ConcurrentDictionary<string, long> _up = new();

    // "ip:port" -> exe  (limitli programlarin aktif uzak uc-noktalari)
    private volatile Dictionary<string, string> _endpointExe = new();

    private readonly object _qLock = new();
    private readonly PriorityQueue<(byte[] pkt, uint len, byte[] addr), long> _queue = new();
    private readonly Dictionary<string, long> _nextAvail = new(); // "exe|d/u" -> ticks
    private const int MaxQueue = 4000;

    public bool HasEnabledLimits => !_down.IsEmpty || !_up.IsEmpty;

    public void SetLimits(IEnumerable<ThrottleRule> rules)
    {
        _down.Clear();
        _up.Clear();
        foreach (var r in rules)
        {
            if (!r.Enabled || string.IsNullOrEmpty(r.Exe)) continue;
            var key = r.Exe.ToLowerInvariant();
            if (r.DownKBps > 0) _down[key] = (long)(r.DownKBps * 1024);
            if (r.UpKBps > 0) _up[key] = (long)(r.UpKBps * 1024);
        }
    }

    public void SetEndpoints(Dictionary<string, string> map) => _endpointExe = map;

    public void Start()
    {
        if (_running) return;
        _handle = WinDivertNative.WinDivertOpen("ip and (tcp or udp) and !loopback",
            WinDivertNative.LayerNetwork, 0, 0);
        if (_handle == WinDivertNative.InvalidHandle)
        {
            int err = Marshal.GetLastWin32Error();
            Error = $"WinDivert açılamadı (kod {err}). Sürücü yüklenemedi olabilir.";
            throw new InvalidOperationException(Error);
        }

        _running = true;
        _recvThread = new Thread(RecvLoop) { IsBackground = true, Name = "WD-Recv" };
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "WD-Worker" };
        _recvThread.Start();
        _workerThread.Start();
    }

    public void Stop()
    {
        if (!_running && _handle == WinDivertNative.InvalidHandle) return;
        _running = false;
        CloseHandle(); // bekleyen Recv hata ile doner
        lock (_qLock) { Monitor.PulseAll(_qLock); }
    }

    private void CloseHandle()
    {
        if (_handle != WinDivertNative.InvalidHandle)
        {
            try { WinDivertNative.WinDivertClose(_handle); } catch { }
            _handle = WinDivertNative.InvalidHandle;
        }
    }

    private void RecvLoop()
    {
        var packet = new byte[65535];
        var addr = new byte[WinDivertNative.AddrSize];

        while (_running)
        {
            uint recvLen;
            bool ok;
            try { ok = WinDivertNative.WinDivertRecv(_handle, packet, (uint)packet.Length, out recvLen, addr); }
            catch { break; }
            if (!ok) { if (!_running) break; continue; }

            try
            {
                bool outbound = WinDivertNative.IsOutbound(addr);
                string? exe = MatchExe(packet, recvLen, outbound);

                long rate = 0;
                if (exe != null)
                {
                    if (outbound) _up.TryGetValue(exe, out rate);
                    else _down.TryGetValue(exe, out rate);
                }

                if (exe == null || rate <= 0)
                {
                    SendNow(packet, recvLen, addr); // sinirsiz: aninda gecir
                    continue;
                }

                var pktCopy = new byte[recvLen];
                Buffer.BlockCopy(packet, 0, pktCopy, 0, (int)recvLen);
                Schedule(exe, outbound, pktCopy, recvLen, (byte[])addr.Clone(), rate);
            }
            catch
            {
                // Herhangi bir hata: paketi dusurme, oldugu gibi gecir.
                try { SendNow(packet, recvLen, addr); } catch { }
            }
        }
    }

    private void SendNow(byte[] pkt, uint len, byte[] addr)
    {
        try { WinDivertNative.WinDivertSend(_handle, pkt, len, out _, addr); } catch { }
    }

    private void Schedule(string exe, bool outbound, byte[] pkt, uint len, byte[] addr, long rate)
    {
        string key = exe + (outbound ? "|u" : "|d");
        long now = DateTime.UtcNow.Ticks;
        lock (_qLock)
        {
            if (_queue.Count >= MaxQueue) { SendNow(pkt, len, addr); return; }
            _nextAvail.TryGetValue(key, out long next);
            long baseT = Math.Max(now, next);
            long dur = (long)((double)len / rate * TimeSpan.TicksPerSecond);
            _nextAvail[key] = baseT + dur;
            _queue.Enqueue((pkt, len, addr), baseT);
            Monitor.PulseAll(_qLock);
        }
    }

    private void WorkerLoop()
    {
        while (_running)
        {
            (byte[] pkt, uint len, byte[] addr) item = default;
            bool has = false;

            lock (_qLock)
            {
                if (_queue.Count == 0)
                {
                    Monitor.Wait(_qLock, 200);
                }
                else if (_queue.TryPeek(out _, out long sendAt))
                {
                    long now = DateTime.UtcNow.Ticks;
                    if (sendAt <= now) { item = _queue.Dequeue(); has = true; }
                    else
                    {
                        long ms = (sendAt - now) / TimeSpan.TicksPerMillisecond;
                        Monitor.Wait(_qLock, (int)Math.Clamp(ms, 1, 500));
                    }
                }
            }

            if (has) SendNow(item.pkt, item.len, item.addr);
        }

        // Durdurulunca kalan paketleri hemen gonder (kaybetme).
        lock (_qLock)
            while (_queue.TryDequeue(out var it, out _))
                SendNow(it.pkt, it.len, it.addr);
    }

    /// <summary>Paketin uzak uc-noktasini cozup limitli exe'ye eslestirir (yoksa null).</summary>
    private string? MatchExe(byte[] p, uint len, bool outbound)
    {
        var map = _endpointExe;
        if (map.Count == 0 || len < 20) return null;

        int ver = p[0] >> 4;
        IPAddress remote;
        int remotePort;

        if (ver == 4)
        {
            int proto = p[9];
            if (proto != 6 && proto != 17) return null;
            int ihl = (p[0] & 0x0F) * 4;
            if (len < ihl + 4) return null;
            var src = new byte[4]; Array.Copy(p, 12, src, 0, 4);
            var dst = new byte[4]; Array.Copy(p, 16, dst, 0, 4);
            int srcPort = (p[ihl] << 8) | p[ihl + 1];
            int dstPort = (p[ihl + 2] << 8) | p[ihl + 3];
            (remote, remotePort) = outbound ? (new IPAddress(dst), dstPort) : (new IPAddress(src), srcPort);
        }
        else if (ver == 6)
        {
            int nextHdr = p[6];
            if (nextHdr != 6 && nextHdr != 17) return null; // uzanti basligi varsa atla
            if (len < 44) return null;
            var src = new byte[16]; Array.Copy(p, 8, src, 0, 16);
            var dst = new byte[16]; Array.Copy(p, 24, dst, 0, 16);
            int srcPort = (p[40] << 8) | p[41];
            int dstPort = (p[42] << 8) | p[43];
            (remote, remotePort) = outbound ? (new IPAddress(dst), dstPort) : (new IPAddress(src), srcPort);
        }
        else return null;

        return map.TryGetValue($"{remote}:{remotePort}", out var exe) ? exe : null;
    }

    public void Dispose() => Stop();
}
