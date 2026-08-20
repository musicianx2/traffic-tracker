using System.Net;
using System.Net.Http;

namespace TrafficTracker.Services;

/// <summary>
/// Bilgisayarin disaridan gorunen (public / WAN) IP adresini birkac ucreetsiz
/// servisten sirayla dener. Herhangi biri cevap verirse dondurur.
/// </summary>
internal static class PublicIpService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private static readonly string[] Endpoints =
    {
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
        "https://ipv4.icanhazip.com"
    };

    public static async Task<string?> GetAsync()
    {
        foreach (var url in Endpoints)
        {
            try
            {
                var text = (await Http.GetStringAsync(url).ConfigureAwait(false)).Trim();
                if (IPAddress.TryParse(text, out _))
                    return text;
            }
            catch
            {
                // Bu servis cevap vermedi: sonrakini dene.
            }
        }
        return null;
    }
}
