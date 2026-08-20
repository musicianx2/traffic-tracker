using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace TrafficTracker.Services;

/// <summary>
/// Program (PID) basina alinan/gonderilen toplam bayt sayisini ETW cekirdek ag
/// olaylariyla toplar — Resource Monitor'un kullandigi yontem. Yonetici gerekir.
/// Kumulatif sayaclar tutulur; anlik hiz, iki okuma farkindan hesaplanir.
/// </summary>
internal sealed class BandwidthMonitor : IDisposable
{
    private TraceEventSession? _session;
    private Thread? _thread;
    private readonly ConcurrentDictionary<int, long> _recv = new();
    private readonly ConcurrentDictionary<int, long> _sent = new();

    public bool Running { get; private set; }
    public string? Error { get; private set; }

    public void Start()
    {
        try
        {
            _session = new TraceEventSession("TrafficTrackerBandwidth");
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var k = _session.Source.Kernel;
            k.TcpIpRecv += d => Add(_recv, d.ProcessID, d.size);
            k.TcpIpRecvIPV6 += d => Add(_recv, d.ProcessID, d.size);
            k.TcpIpSend += d => Add(_sent, d.ProcessID, d.size);
            k.TcpIpSendIPV6 += d => Add(_sent, d.ProcessID, d.size);
            k.UdpIpRecv += d => Add(_recv, d.ProcessID, d.size);
            k.UdpIpRecvIPV6 += d => Add(_recv, d.ProcessID, d.size);
            k.UdpIpSend += d => Add(_sent, d.ProcessID, d.size);
            k.UdpIpSendIPV6 += d => Add(_sent, d.ProcessID, d.size);

            _thread = new Thread(() =>
            {
                try { _session.Source.Process(); }
                catch { /* oturum kapaninca cikar */ }
            })
            { IsBackground = true, Name = "ETW-Bandwidth" };
            _thread.Start();

            Running = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Running = false;
        }
    }

    private static void Add(ConcurrentDictionary<int, long> dict, int pid, int size)
    {
        if (pid <= 0 || size <= 0) return;
        dict.AddOrUpdate(pid, size, (_, v) => v + size);
    }

    /// <summary>PID icin (alinan, gonderilen) toplam bayt.</summary>
    public (long recv, long sent) Get(int pid)
        => (_recv.TryGetValue(pid, out var r) ? r : 0,
            _sent.TryGetValue(pid, out var s) ? s : 0);

    public void Dispose()
    {
        try
        {
            Running = false;
            _session?.Dispose();
            _session = null;
        }
        catch { /* yoksay */ }
    }
}
