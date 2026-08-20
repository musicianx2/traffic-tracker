using System.Diagnostics;
using System.Text.RegularExpressions;
using TrafficTracker.Models;

namespace TrafficTracker.Services;

/// <summary>
/// Windows Update ile ilgili servisleri ve politika anahtarlarini yonetir.
/// Tum islemler yerlesik 'sc' ve 'reg' araclariyla yapilir (harici bagimlilik yok).
/// Uygulama yonetici oldugundan bu alt-islemler de yonetici calisir.
///
/// Kilitle: servisleri devre disi birak + durdur, NoAutoUpdate=1, Store indirme=kapali.
/// Kilidi ac: servisleri varsayilana dondur, politika anahtarlarini kaldir.
/// </summary>
internal static class WindowsUpdateManager
{
    // (servis adi, aciklama, kilit-acinca-donecegi baslangic turu)
    public static readonly (string Name, string Desc, string UnlockStart)[] Services =
    {
        ("wuauserv",     "Windows Update",                "demand"),
        ("UsoSvc",       "Update Orchestrator (indirme)", "auto"),
        ("WaaSMedicSvc", "Update Medic (kilidi açan)",    "demand"),
        ("dosvc",        "Delivery Optimization",         "demand"),
    };

    private const string AuKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string StoreKey = @"HKLM\SOFTWARE\Policies\Microsoft\WindowsStore";

    // -------------------------------------------------------------- alt-islem

    private static (int exit, string output) Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    // -------------------------------------------------------------- okuma

    public static List<UpdateServiceState> GetStatus()
    {
        var list = new List<UpdateServiceState>();
        foreach (var (name, desc, _) in Services)
        {
            var (regExit, regOut) = Run("reg",
                $@"query ""HKLM\SYSTEM\CurrentControlSet\Services\{name}"" /v Start");
            int start = -1;
            var m = Regex.Match(regOut, @"Start\s+REG_DWORD\s+0x([0-9a-fA-F]+)");
            if (m.Success) start = Convert.ToInt32(m.Groups[1].Value, 16);

            var (_, scOut) = Run("sc", $"query {name}");
            bool running = scOut.Contains("RUNNING");
            bool accessible = regExit == 0 || m.Success;

            list.Add(new UpdateServiceState
            {
                Name = name,
                Description = desc,
                StartValue = start,
                Running = running,
                Accessible = accessible
            });
        }
        return list;
    }

    public static bool IsNoAutoUpdate()
    {
        var (_, o) = Run("reg", $@"query ""{AuKey}"" /v NoAutoUpdate");
        var m = Regex.Match(o, @"NoAutoUpdate\s+REG_DWORD\s+0x([0-9a-fA-F]+)");
        return m.Success && Convert.ToInt32(m.Groups[1].Value, 16) == 1;
    }

    public static bool IsStoreAutoDownloadBlocked()
    {
        var (_, o) = Run("reg", $@"query ""{StoreKey}"" /v AutoDownload");
        var m = Regex.Match(o, @"AutoDownload\s+REG_DWORD\s+0x([0-9a-fA-F]+)");
        return m.Success && Convert.ToInt32(m.Groups[1].Value, 16) == 2;
    }

    // -------------------------------------------------------------- eylemler

    private static bool SetStart(string svc, string type)
        => Run("sc", $"config {svc} start= {type}").exit == 0;

    private static void Stop(string svc) => Run("sc", $"stop {svc}");
    private static void Start(string svc) => Run("sc", $"start {svc}");

    // -------------------------------------------------------------- tek servis (sag tik menusu)

    public static string DisableAndStop(string svc)
    {
        bool ok = SetStart(svc, "disabled");
        Stop(svc);
        return ok
            ? $"✔ {svc}: devre dışı bırakıldı ve durduruldu"
            : $"✖ {svc}: erişim reddedildi (korumalı servis; zaten devre dışıysa sorun değil)";
    }

    public static string SetStartType(string svc, string type)
    {
        bool ok = SetStart(svc, type);
        return ok ? $"✔ {svc}: başlangıç türü '{type}' yapıldı" : $"✖ {svc}: erişim reddedildi";
    }

    public static string StopOne(string svc)
    {
        Stop(svc);
        return $"⏹ {svc}: durdurma denendi";
    }

    public static string StartOne(string svc)
    {
        var (exit, outp) = Run("sc", $"start {svc}");
        return exit == 0
            ? $"▶ {svc}: başlatıldı"
            : $"✖ {svc}: başlatılamadı (devre dışıysa önce başlangıç türünü değiştir)";
    }

    /// <summary>Guncellemeleri kilitler. Adim adim sonuc raporu doner.</summary>
    public static List<string> Lock()
    {
        var log = new List<string>();

        foreach (var (name, desc, _) in Services)
        {
            bool ok = SetStart(name, "disabled");
            Stop(name);
            log.Add(ok
                ? $"✔ {name} ({desc}) → devre dışı bırakıldı"
                : $"✖ {name} ({desc}) → erişim reddedildi (korumalı; mevcut hâli korunuyor)");
        }

        Run("reg", $@"add ""{AuKey}"" /v NoAutoUpdate /t REG_DWORD /d 1 /f");
        log.Add("✔ Politika: otomatik güncelleme kapatıldı (NoAutoUpdate=1)");

        Run("reg", $@"add ""{StoreKey}"" /v AutoDownload /t REG_DWORD /d 2 /f");
        log.Add("✔ Store otomatik güncelleme kapatıldı");

        return log;
    }

    /// <summary>Kilidi acar: servisleri varsayilana dondurur, politikalari kaldirir.</summary>
    public static List<string> Unlock()
    {
        var log = new List<string>();

        foreach (var (name, desc, unlockStart) in Services)
        {
            bool ok = SetStart(name, unlockStart);
            log.Add(ok
                ? $"✔ {name} ({desc}) → {unlockStart} yapıldı"
                : $"✖ {name} ({desc}) → erişim reddedildi (korumalı)");
        }

        Run("reg", $@"delete ""{AuKey}"" /v NoAutoUpdate /f");
        log.Add("✔ NoAutoUpdate politikası kaldırıldı");

        Run("reg", $@"delete ""{StoreKey}"" /v AutoDownload /f");
        log.Add("✔ Store otomatik güncelleme politikası kaldırıldı");

        // Windows Update'i tekrar tetiklenebilir hale getir.
        Start("wuauserv");
        log.Add("ℹ Not: güncellemeleri şimdi Ayarlar → Windows Update'ten elle başlatabilirsiniz.");

        return log;
    }
}
