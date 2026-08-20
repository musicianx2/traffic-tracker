using TrafficTracker.Models;

namespace TrafficTracker.Services;

/// <summary>
/// Engelleme kurallarini Windows Guvenlik Duvari'na (INetFwPolicy2 COM API)
/// yansitir. Harici surucu/indirme gerektirmez; kurallar kalicidir.
/// Tum islemler kural adiyla yapilir (enumerasyon yok).
/// </summary>
internal static class FirewallManager
{
    private const int ActionBlock = 0;
    private const int DirIn = 1;
    private const int DirOut = 2;
    private const int ProtoTcp = 6;
    private const int ProtoUdp = 17;
    private const int ProtoAny = 256;
    private const int ProfileAll = 0x7FFFFFFF;

    private static dynamic CreatePolicy()
    {
        var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                ?? throw new InvalidOperationException("Windows Güvenlik Duvarı COM API bulunamadı.");
        return Activator.CreateInstance(t)!;
    }

    private static dynamic CreateRule()
    {
        var t = Type.GetTypeFromProgID("HNetCfg.FWRule")
                ?? throw new InvalidOperationException("Güvenlik duvarı kuralı oluşturulamadı.");
        return Activator.CreateInstance(t)!;
    }

    // Bir kuralin uretecegi (yon, protokol) kombinasyonlari.
    private static IEnumerable<(int dir, int proto)> Combos(BlockRule rule)
    {
        int[] dirs = rule.Direction switch
        {
            RuleDirection.Outbound => new[] { DirOut },
            RuleDirection.Inbound => new[] { DirIn },
            _ => new[] { DirOut, DirIn }
        };

        int[] protos = rule.Kind switch
        {
            BlockKind.App => new[] { ProtoAny },
            BlockKind.RemoteIp => new[] { ProtoNum(rule.Protocol) },
            BlockKind.RemotePort => rule.Protocol == RuleProtocol.Any
                ? new[] { ProtoTcp, ProtoUdp }
                : new[] { ProtoNum(rule.Protocol) },
            _ => new[] { ProtoAny }
        };

        foreach (var d in dirs)
            foreach (var p in protos)
                yield return (d, p);
    }

    private static int ProtoNum(RuleProtocol p) => p switch
    {
        RuleProtocol.Tcp => ProtoTcp,
        RuleProtocol.Udp => ProtoUdp,
        _ => ProtoAny
    };

    private static string SubName(string baseName, int dir, int proto) => $"{baseName} [{dir}/{proto}]";

    /// <summary>Kuralin tum olasi alt-kural adlarini (6 kombinasyon) siler. Hata yoksayilir.</summary>
    private static void RemoveAllVariants(dynamic policy, string baseName)
    {
        foreach (var dir in new[] { DirIn, DirOut })
            foreach (var proto in new[] { ProtoTcp, ProtoUdp, ProtoAny })
            {
                try { policy.Rules.Remove(SubName(baseName, dir, proto)); }
                catch { /* yok: yoksay */ }
            }
    }

    /// <summary>Kurali guvenlik duvarina (yeniden) yazar.</summary>
    public static void Apply(BlockRule rule)
    {
        var policy = CreatePolicy();
        RemoveAllVariants(policy, rule.FirewallBaseName);

        foreach (var (dir, proto) in Combos(rule))
        {
            dynamic r = CreateRule();
            r.Name = SubName(rule.FirewallBaseName, dir, proto);
            r.Description = $"Traffic Tracker · {rule.KindText}: {rule.DisplayTarget}" +
                            (string.IsNullOrEmpty(rule.Note) ? "" : $" · {rule.Note}");
            r.Action = ActionBlock;
            r.Direction = dir;
            r.Enabled = rule.Enabled;
            r.Profiles = ProfileAll;
            r.Protocol = proto;

            switch (rule.Kind)
            {
                case BlockKind.App:
                    r.ApplicationName = rule.Target;
                    break;
                case BlockKind.RemoteIp:
                    r.RemoteAddresses = rule.Target;
                    break;
                case BlockKind.RemotePort:
                    r.RemotePorts = rule.Target;
                    break;
            }

            policy.Rules.Add(r);
        }
    }

    /// <summary>Kuralin tum alt-kurallarini guvenlik duvarindan siler.</summary>
    public static void Delete(BlockRule rule)
    {
        var policy = CreatePolicy();
        RemoveAllVariants(policy, rule.FirewallBaseName);
    }

    /// <summary>Kurali guvenlik duvarinda etkinlestirir/devre disi birakir.</summary>
    public static void SetEnabled(BlockRule rule, bool enabled)
    {
        var policy = CreatePolicy();
        foreach (var (dir, proto) in Combos(rule))
        {
            try
            {
                dynamic r = policy.Rules.Item(SubName(rule.FirewallBaseName, dir, proto));
                r.Enabled = enabled;
            }
            catch
            {
                // Kural yoksa yeniden olustur.
                Apply(rule);
                return;
            }
        }
    }
}
