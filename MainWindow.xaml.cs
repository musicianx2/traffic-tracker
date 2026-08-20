using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TrafficTracker.Models;
using TrafficTracker.Native;
using TrafficTracker.Services;

namespace TrafficTracker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ConnectionRow> _rows = new();
    private readonly Dictionary<string, ConnectionRow> _map = new();
    private readonly ObservableCollection<BlockRule> _rules = new();
    private readonly ObservableCollection<HistoryEntry> _history = new();
    private readonly ObservableCollection<UpdateServiceState> _svc = new();
    private readonly DnsResolver _dns = new();
    private readonly DispatcherTimer _timer = new();
    private ICollectionView _view = null!;
    private int _generation;
    private bool _elevated;

    // Canli hiz grafigi
    private readonly DispatcherTimer _netTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<double> _down = new();
    private readonly List<double> _up = new();
    private long _lastRecv = -1, _lastSent = -1;
    private const int MaxSamples = 120;

    // Sistem tepsisi
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _balloonShown;

    // Program bazli bant genisligi
    private readonly BandwidthMonitor _bw = new();
    private Dictionary<int, (long recv, long sent)> _bwPrev = new();
    private readonly Dictionary<int, (double down, double up)> _rateByPid = new();
    private Dictionary<int, (double down, int staleD, double up, int staleU)> _disp = new();
    private const int BwHoldTicks = 4; // trafik durunca son degeri ~4 sn tut (titremeyi onler)
    private AppSettings _settings = new();

    // Program hiz sinirlama (WinDivert)
    private readonly ThrottleEngine _throttle = new();
    private readonly ObservableCollection<ThrottleRule> _throttles = new();
    private readonly Dictionary<int, string?> _pathCache = new();

    public MainWindow()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ConnGrid.ItemsSource = _view;
        RulesGrid.ItemsSource = _rules;
        HistoryGrid.ItemsSource = _history;
        UpdateGrid.ItemsSource = _svc;

        foreach (var h in HistoryStore.Load())
            _history.Add(h);

        foreach (var (label, seconds) in new[] { ("1 sn", 1), ("2 sn", 2), ("3 sn", 3), ("5 sn", 5) })
            IntervalCombo.Items.Add(new ComboBoxItem { Content = label, Tag = seconds });
        IntervalCombo.SelectedIndex = 1;

        _elevated = IsElevated();
        if (!_elevated)
            AdminHint.Text = "⚠  Yönetici değil — engelleme çalışmaz. Uygulamayı yönetici olarak başlatın.";

        LoadRules();

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
        Loaded += async (_, _) => await RefreshPublicIpAsync();
        _timer.Start();

        _netTimer.Tick += (_, _) => SampleSpeed();
        _netTimer.Start();
        SpeedCanvas.SizeChanged += (_, _) => RedrawGraph();

        SetupTray();

        // Ayarlar + bant genisligi olcumu
        _settings = SettingsStore.Load();
        ThresholdBox.Text = _settings.ThresholdKBps.ToString("0.###");
        HighlightCheck.IsChecked = _settings.HighlightEnabled;

        if (_elevated)
        {
            _bw.Start();
            BandwidthInfo.Text = _bw.Running
                ? "✔ Bant genişliği ölçümü aktif (ETW). '↓/s' ve '↑/s' kolonları her programın anlık hızını gösterir. Başlık'a tıklayıp yükseğe göre sıralayabilirsin."
                : $"⚠ Bant genişliği ölçümü başlatılamadı: {_bw.Error}";
        }
        else
        {
            BandwidthInfo.Text = "⚠ Bant genişliği ölçümü için uygulama yönetici çalışmalı.";
        }

        // Hiz sinirlari
        foreach (var t in ThrottleStore.Load())
        {
            t.PropertyChanged += Throttle_PropertyChanged;
            _throttles.Add(t);
        }
        ThrottleGrid.ItemsSource = _throttles;
        ThrottleEnableCheck.IsChecked = _settings.ThrottleEnabled;
        if (_settings.ThrottleEnabled && _elevated)
            StartThrottling();
    }

    // ---------------------------------------------------------------- Hiz sinirlama (WinDivert)

    private void ThrottleApp_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureElevated()) return;
        if (SelectedConn() is not { } row) return;

        var path = ProcessPath.Get(row.Pid);
        if (string.IsNullOrEmpty(path))
        {
            Info($"'{row.Process}' programının dosya yolu okunamadı; hız sınırı koyulamıyor.");
            return;
        }

        var key = path.ToLowerInvariant();
        if (_throttles.Any(t => t.Exe.ToLowerInvariant() == key))
        {
            Info("Bu program için zaten bir hız sınırı var (Ayarlar sekmesinden düzenle).");
            MainTabs.SelectedIndex = 4;
            return;
        }

        var rule = new ThrottleRule
        {
            Exe = path,
            DisplayName = System.IO.Path.GetFileName(path),
            DownKBps = 500,
            UpKBps = 500,
            Enabled = true
        };
        rule.PropertyChanged += Throttle_PropertyChanged;
        _throttles.Add(rule);
        ThrottleStore.Save(_throttles);

        if (!_throttle.Running)
        {
            ThrottleEnableCheck.IsChecked = true;
            _settings.ThrottleEnabled = true;
            SettingsStore.Save(_settings);
            StartThrottling();
        }
        else
        {
            _throttle.SetLimits(_throttles);
        }

        StatusText.Text = $"Hız sınırı eklendi: {rule.DisplayName} (500/500 KB/s). Ayarlar'dan değiştirebilirsin.";
        MainTabs.SelectedIndex = 4;
    }

    private void Throttle_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _throttle.SetLimits(_throttles);
        ThrottleStore.Save(_throttles);
    }

    private void ThrottleToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = ThrottleEnableCheck.IsChecked == true;
        if (on)
        {
            if (!_elevated) { ThrottleEnableCheck.IsChecked = false; EnsureElevated(); return; }
            StartThrottling();
        }
        else
        {
            _throttle.Stop();
            ThrottleStatus.Text = "Kapalı — ağ normal.";
        }
        _settings.ThrottleEnabled = ThrottleEnableCheck.IsChecked == true;
        SettingsStore.Save(_settings);
    }

    private void StartThrottling()
    {
        try
        {
            _throttle.SetLimits(_throttles);
            _throttle.Start();
            ThrottleStatus.Text = "✔ Etkin (WinDivert). Sınırlar uygulanıyor.";
        }
        catch (Exception ex)
        {
            ThrottleEnableCheck.IsChecked = false;
            ThrottleStatus.Text = "Başlatılamadı.";
            MessageBox.Show($"Hız sınırlama başlatılamadı:\n{ex.Message}", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteThrottle_Click(object sender, RoutedEventArgs e)
    {
        if (ThrottleGrid.SelectedItem is not ThrottleRule rule)
        {
            Info("Önce silinecek sınırı seçin.");
            return;
        }
        rule.PropertyChanged -= Throttle_PropertyChanged;
        _throttles.Remove(rule);
        ThrottleStore.Save(_throttles);
        _throttle.SetLimits(_throttles);
        StatusText.Text = $"Hız sınırı silindi: {rule.DisplayName}";
    }

    private string? ResolvePath(int pid)
    {
        if (_pathCache.TryGetValue(pid, out var p)) return p;
        p = ProcessPath.Get(pid);
        _pathCache[pid] = p;
        return p;
    }

    /// <summary>Limitli programlarin aktif uzak uc-noktalarini motora bildirir.</summary>
    private void FeedThrottleEndpoints()
    {
        if (!_throttle.Running) return;

        var limited = _throttles.Where(t => t.Enabled && !string.IsNullOrEmpty(t.Exe))
                                 .Select(t => t.Exe.ToLowerInvariant())
                                 .ToHashSet();

        var map = new Dictionary<string, string>();
        if (limited.Count > 0)
        {
            foreach (var row in _rows)
            {
                if (row.RemotePort <= 0 || string.IsNullOrEmpty(row.RemoteAddress)) continue;
                var path = ResolvePath(row.Pid);
                if (path == null) continue;
                var pl = path.ToLowerInvariant();
                if (!limited.Contains(pl)) continue;
                map[$"{row.RemoteAddress}:{row.RemotePort}"] = pl;
            }
        }
        _throttle.SetEndpoints(map);
    }

    // ---------------------------------------------------------------- Canli hiz grafigi

    private void SampleSpeed()
    {
        var (recv, sent) = NetSpeedService.TotalBytes();
        if (_lastRecv >= 0)
        {
            double down = Math.Max(0, recv - _lastRecv);
            double up = Math.Max(0, sent - _lastSent);
            Push(_down, down);
            Push(_up, up);
            DownText.Text = NetSpeedService.Format(down);
            UpText.Text = NetSpeedService.Format(up);
            RedrawGraph();
        }
        _lastRecv = recv;
        _lastSent = sent;

        UpdatePerProcessBandwidth();
    }

    /// <summary>PID basina kumulatif baytlari okuyup anlik hiza cevirir, satirlara yansitir.</summary>
    private void UpdatePerProcessBandwidth()
    {
        if (!_bw.Running) return;

        var newPrev = new Dictionary<int, (long recv, long sent)>();
        var newDisp = new Dictionary<int, (double down, int staleD, double up, int staleU)>();
        _rateByPid.Clear();

        foreach (var pid in _rows.Select(r => r.Pid).Distinct())
        {
            var (recv, sent) = _bw.Get(pid);
            _bwPrev.TryGetValue(pid, out var prev);
            // net timer araligi ~1 sn oldugundan fark = bayt/sn
            double instDown = Math.Max(0, recv - prev.recv);
            double instUp = Math.Max(0, sent - prev.sent);
            newPrev[pid] = (recv, sent);

            // Trafik olmayan saniyede son degeri tut (BwHoldTicks kadar), sonra sifirla.
            _disp.TryGetValue(pid, out var d);
            var (down, staleD) = Hold(instDown, d.down, d.staleD);
            var (up, staleU) = Hold(instUp, d.up, d.staleU);
            newDisp[pid] = (down, staleD, up, staleU);
            _rateByPid[pid] = (down, up);
        }
        _bwPrev = newPrev;
        _disp = newDisp;

        double thresholdBytes = _settings.ThresholdKBps * 1024;
        foreach (var row in _rows)
        {
            var (down, up) = _rateByPid.TryGetValue(row.Pid, out var v) ? v : (0d, 0d);
            row.DownRate = down;
            row.UpRate = up;
            row.OverThreshold = _settings.HighlightEnabled && thresholdBytes > 0 && (down + up) >= thresholdBytes;
        }
    }

    private static (double val, int stale) Hold(double instant, double prevVal, int prevStale)
    {
        if (instant > 0) return (instant, 0);
        int stale = prevStale + 1;
        return stale <= BwHoldTicks ? (prevVal, stale) : (0d, stale);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(ThresholdBox.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var kbps) || kbps < 0)
        {
            SettingsStatus.Foreground = (System.Windows.Media.Brush)FindResource("Warn");
            SettingsStatus.Text = "  Geçersiz sayı.";
            return;
        }

        _settings.ThresholdKBps = kbps;
        _settings.HighlightEnabled = HighlightCheck.IsChecked == true;
        SettingsStore.Save(_settings);

        SettingsStatus.Foreground = (System.Windows.Media.Brush)FindResource("Good");
        SettingsStatus.Text = $"  Kaydedildi: eşik {kbps:0.###} KB/s.";
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > MaxSamples) list.RemoveAt(0);
    }

    private void RedrawGraph()
    {
        double w = SpeedCanvas.ActualWidth, h = SpeedCanvas.ActualHeight;
        if (w <= 1 || h <= 1 || _down.Count < 2)
        {
            DownLine.Points = new PointCollection();
            UpLine.Points = new PointCollection();
            return;
        }

        double peak = Math.Max(1024, Math.Max(_down.Max(), _up.Max()));
        DownLine.Points = BuildPoints(_down, w, h, peak);
        UpLine.Points = BuildPoints(_up, w, h, peak);
    }

    private static PointCollection BuildPoints(List<double> data, double w, double h, double peak)
    {
        var pts = new PointCollection();
        int n = data.Count;
        for (int i = 0; i < n; i++)
        {
            double x = (double)i / (n - 1) * w;
            double y = h - (data[i] / peak) * (h - 4) - 2;
            pts.Add(new Point(x, y));
        }
        return pts;
    }

    // ---------------------------------------------------------------- Sistem tepsisi (tray)

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "Traffic Tracker"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Göster", null, (_, _) => ShowFromTray());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) return;
            Hide();
            if (!_balloonShown)
            {
                _tray?.ShowBalloonTip(3000, "Traffic Tracker",
                    "Arka planda çalışıyorum. Simgeye çift tıkla → geri aç.",
                    System.Windows.Forms.ToolTipIcon.Info);
                _balloonShown = true;
            }
        };
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _bw.Dispose();
        _throttle.Dispose();
        base.OnClosed(e);
    }

    // ---------------------------------------------------------------- Dis (public) IP

    private async Task RefreshPublicIpAsync()
    {
        PublicIpText.Text = "alınıyor…";
        var ip = await PublicIpService.GetAsync();
        PublicIpText.Text = ip ?? "alınamadı";
    }

    private async void RefreshPublicIp_Click(object sender, RoutedEventArgs e)
        => await RefreshPublicIpAsync();

    private void PublicIp_Copy(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            try { Clipboard.SetText(PublicIpText.Text); } catch { /* pano kilitli olabilir */ }
    }

    // ---------------------------------------------------------------- Kurallar

    private void LoadRules()
    {
        foreach (var rule in RuleStore.Load())
        {
            rule.PropertyChanged += Rule_PropertyChanged;
            _rules.Add(rule);

            // Guvenlik duvarinda tutarli oldugundan emin ol.
            if (_elevated)
                TrySafe(() => FirewallManager.Apply(rule), "Kural uygulanamadı");
        }
    }

    private void Rule_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BlockRule rule || e.PropertyName != nameof(BlockRule.Enabled))
            return;

        if (_elevated)
            TrySafe(() => FirewallManager.SetEnabled(rule, rule.Enabled), "Kural durumu değiştirilemedi");

        RuleStore.Save(_rules);
        LogHistory(rule.Enabled ? HistoryAction.Enabled : HistoryAction.Disabled, rule);
    }

    private void AddRule(BlockRule rule)
    {
        if (!EnsureElevated()) return;

        rule.PropertyChanged += Rule_PropertyChanged;
        _rules.Add(rule);

        if (TrySafe(() => FirewallManager.Apply(rule), "Kural eklenemedi"))
        {
            RuleStore.Save(_rules);
            LogHistory(HistoryAction.Added, rule);
            StatusText.Text = $"Kural eklendi: {rule.KindText} → {rule.DisplayTarget}";
        }
        else
        {
            rule.PropertyChanged -= Rule_PropertyChanged;
            _rules.Remove(rule);
        }
    }

    private void RemoveRule(BlockRule rule)
    {
        if (_elevated)
            TrySafe(() => FirewallManager.Delete(rule), "Kural silinemedi");

        rule.PropertyChanged -= Rule_PropertyChanged;
        _rules.Remove(rule);
        RuleStore.Save(_rules);
        LogHistory(HistoryAction.Deleted, rule);
        StatusText.Text = $"Kural silindi: {rule.KindText} → {rule.DisplayTarget}";
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not BlockRule rule)
        {
            MessageBox.Show("Önce silinecek kuralı seçin.", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RemoveRule(rule);
    }

    // ---------------------------------------------------------------- Gecmis

    private void LogHistory(HistoryAction action, BlockRule rule)
    {
        _history.Insert(0, HistoryEntry.From(action, rule));
        HistoryStore.Save(_history);
    }

    private void RevertHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryEntry entry)
        {
            MessageBox.Show("Önce geri alınacak işlemi seçin.", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureElevated()) return;

        var live = _rules.FirstOrDefault(r => r.Id == entry.RuleId);

        switch (entry.Action)
        {
            case HistoryAction.Added: // ekleme -> geri al: sil
                if (live != null) RemoveRule(live);
                else Info("Bu kural zaten kaldırılmış.");
                break;

            case HistoryAction.Deleted: // silme -> geri al: yeniden ekle
                if (live != null) Info("Bu kural şu an zaten mevcut.");
                else AddRule(entry.ToRule());
                break;

            case HistoryAction.Enabled: // açma -> geri al: kapat
                if (live != null) live.Enabled = false;
                else Info("Kural artık mevcut değil.");
                break;

            case HistoryAction.Disabled: // kapatma -> geri al: aç
                if (live != null) live.Enabled = true;
                else Info("Kural artık mevcut değil.");
                break;
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;
        if (MessageBox.Show("Tüm işlem geçmişi silinsin mi? (Kurallar etkilenmez.)",
                "Traffic Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _history.Clear();
        HistoryStore.Save(_history);
    }

    // ---------------------------------------------------------------- Windows Update

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Sadece Windows Update sekmesine gecince durumu tazele (pahali olmasin).
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
        if (MainTabs.SelectedItem is TabItem { Header: "Windows Update" })
            RefreshUpdateStatus();
    }

    private void RefreshUpdates_Click(object sender, RoutedEventArgs e) => RefreshUpdateStatus();

    private void RefreshUpdateStatus()
    {
        try
        {
            _svc.Clear();
            foreach (var s in WindowsUpdateManager.GetStatus())
                _svc.Add(s);

            bool lockedCount = _svc.Count > 0 && _svc.All(s => !s.Accessible || s.IsLocked);
            string au = WindowsUpdateManager.IsNoAutoUpdate() ? "kapalı" : "açık";
            string store = WindowsUpdateManager.IsStoreAutoDownloadBlocked() ? "kapalı" : "açık";

            UpdatePolicyText.Text =
                $"Genel durum: {(lockedCount ? "🔒 KİLİTLİ" : "🔓 açık")}  ·  " +
                $"Otomatik güncelleme politikası: {au}  ·  Store otomatik indirme: {store}";
        }
        catch (Exception ex)
        {
            UpdatePolicyText.Text = $"Durum okunamadı: {ex.Message}";
        }
    }

    private void LockUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureElevated()) return;
        if (MessageBox.Show(
                "Windows Update servisleri devre dışı bırakılacak ve otomatik güncelleme kapatılacak.\n\n" +
                "Güncellemeleri daha sonra 'Kilidi Aç' ile geri açabilirsin. Devam edilsin mi?",
                "Traffic Tracker — Güncellemeleri Kilitle",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        List<string> log = new();
        TrySafe(() => log = WindowsUpdateManager.Lock(), "Kilitleme başarısız");
        UpdateLogText.Text = string.Join("\n", log);
        RefreshUpdateStatus();
        StatusText.Text = "Windows Update kilitlendi.";
    }

    private void UnlockUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureElevated()) return;
        if (MessageBox.Show(
                "Windows Update servisleri varsayılana döndürülecek ve güncellemeye tekrar izin verilecek.\nDevam edilsin mi?",
                "Traffic Tracker — Kilidi Aç",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        List<string> log = new();
        TrySafe(() => log = WindowsUpdateManager.Unlock(), "Kilit açma başarısız");
        UpdateLogText.Text = string.Join("\n", log);
        RefreshUpdateStatus();
        StatusText.Text = "Windows Update kilidi açıldı.";
    }

    private static void Info(string message)
        => MessageBox.Show(message, "Traffic Tracker", MessageBoxButton.OK, MessageBoxImage.Information);

    // --- Tek servis (Windows Update sekmesi sag tik) ---

    private UpdateServiceState? SelectedSvc()
    {
        if (UpdateGrid.SelectedItem is UpdateServiceState s) return s;
        Info("Önce listeden bir servis seçin.");
        return null;
    }

    private void RunSvcAction(Func<string, string> action)
    {
        if (!EnsureElevated()) return;
        if (SelectedSvc() is not { } s) return;
        string result = "";
        TrySafe(() => result = action(s.Name), "Servis işlemi başarısız");
        UpdateLogText.Text = result;
        RefreshUpdateStatus();
        StatusText.Text = result;
    }

    private void SvcDisable_Click(object sender, RoutedEventArgs e) => RunSvcAction(WindowsUpdateManager.DisableAndStop);
    private void SvcStop_Click(object sender, RoutedEventArgs e) => RunSvcAction(WindowsUpdateManager.StopOne);
    private void SvcStart_Click(object sender, RoutedEventArgs e) => RunSvcAction(WindowsUpdateManager.StartOne);
    private void SvcManual_Click(object sender, RoutedEventArgs e) => RunSvcAction(s => WindowsUpdateManager.SetStartType(s, "demand"));
    private void SvcAuto_Click(object sender, RoutedEventArgs e) => RunSvcAction(s => WindowsUpdateManager.SetStartType(s, "auto"));

    private void ReapplyAll_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureElevated()) return;

        foreach (var rule in _rules)
            TrySafe(() => FirewallManager.Apply(rule), "Kural uygulanamadı");

        StatusText.Text = $"{_rules.Count} kural yeniden uygulandı.";
    }

    // ---------------------------------------------------------- Engelleme aksiyonlari

    private void BlockApp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConn() is not { } row) return;

        var path = ProcessPath.Get(row.Pid);
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show(
                $"'{row.Process}' (PID {row.Pid}) programının dosya yolu okunamadı.\n" +
                "Bu programı engelleyemiyorum; bunun yerine IP veya port engelleyebilirsiniz.",
                "Traffic Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Confirm($"'{System.IO.Path.GetFileName(path)}' programının TÜM ağ trafiği engellensin mi?\n\n{path}"))
        {
            AddRule(new BlockRule
            {
                Kind = BlockKind.App,
                Target = path,
                Protocol = RuleProtocol.Any,
                Direction = RuleDirection.Both,
                Note = path
            });
        }
    }

    private void BlockIp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConn() is not { } row) return;

        if (string.IsNullOrEmpty(row.RemoteAddress) || IsLocalOnly(row))
        {
            MessageBox.Show("Bu satırda engellenecek geçerli bir uzak IP yok.", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var host = string.IsNullOrEmpty(row.Host) ? "" : $" ({row.Host})";
        if (Confirm($"{row.RemoteAddress}{host} adresine giden/gelen tüm trafik engellensin mi?"))
        {
            AddRule(new BlockRule
            {
                Kind = BlockKind.RemoteIp,
                Target = row.RemoteAddress,
                Protocol = RuleProtocol.Any,
                Direction = RuleDirection.Both,
                Note = string.IsNullOrEmpty(row.Host) ? row.Process : $"{row.Host} · {row.Process}"
            });
        }
    }

    private void BlockPort_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConn() is not { } row) return;

        if (row.RemotePort <= 0)
        {
            MessageBox.Show("Bu satırda engellenecek geçerli bir uzak port yok.", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var proto = row.Protocol == "UDP" ? RuleProtocol.Udp : RuleProtocol.Tcp;
        if (Confirm($"{proto.ToString().ToUpper()} {row.RemotePort} portuna giden/gelen tüm trafik engellensin mi?"))
        {
            AddRule(new BlockRule
            {
                Kind = BlockKind.RemotePort,
                Target = row.RemotePort.ToString(),
                Protocol = proto,
                Direction = RuleDirection.Both,
                Note = $"{row.Process} üzerinden görüldü"
            });
        }
    }

    private void CopyRemote_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConn() is not { } row) return;
        try { Clipboard.SetText(row.RemoteAddress); } catch { /* pano kilitli olabilir */ }
    }

    // ---------------------------------------------------------------- Izleme dongusu

    private void Refresh()
    {
        if (PauseCheck.IsChecked == true) return;

        _generation++;
        List<ConnectionInfo> conns;
        try
        {
            conns = IpHelper.GetAllConnections();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Bağlantılar okunamadı: {ex.Message}";
            return;
        }

        var procs = ProcessResolver.Snapshot();
        int established = 0;

        foreach (var c in conns)
        {
            if (c.Protocol == Protocol.Tcp && c.State == "ESTABLISHED") established++;

            string procName = procs.TryGetValue(c.Pid, out var n) && !string.IsNullOrEmpty(n)
                ? n
                : $"PID {c.Pid}";

            if (_map.TryGetValue(c.Key, out var row))
            {
                row.Process = procName;
                row.State = c.State;
                row.LastSeenTick = _generation;
            }
            else
            {
                row = new ConnectionRow
                {
                    Key = c.Key,
                    Protocol = c.Protocol == Protocol.Tcp ? "TCP" : "UDP",
                    LocalEndpoint = FormatEndpoint(c.LocalAddress, c.LocalPort),
                    RemoteAddress = c.RemoteAddress?.ToString() ?? "",
                    RemotePort = c.RemotePort,
                    Pid = c.Pid,
                    Process = procName,
                    State = c.State,
                    LastSeenTick = _generation
                };
                _map[c.Key] = row;
                _rows.Add(row);
            }

            var host = _dns.GetHost(c.RemoteAddress);
            if (!string.IsNullOrEmpty(host))
                row.Host = host!;
        }

        var stale = _map.Values.Where(r => r.LastSeenTick != _generation).ToList();
        foreach (var r in stale)
        {
            _map.Remove(r.Key);
            _rows.Remove(r);
        }

        _view.Refresh();

        int shown = 0;
        foreach (var _ in _view) shown++;

        StatusText.Text =
            $"Toplam {_rows.Count} bağlantı · gösterilen {shown} · ESTABLISHED {established} · " +
            $"{_rules.Count} kural · son güncelleme {DateTime.Now:HH:mm:ss}";

        FeedThrottleEndpoints();
    }

    // ---------------------------------------------------------------- Yardimcilar

    private ConnectionRow? SelectedConn()
    {
        if (ConnGrid.SelectedItem is ConnectionRow row) return row;
        MessageBox.Show("Önce bir bağlantı seçin.", "Traffic Tracker",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private bool FilterRow(object o)
    {
        var r = (ConnectionRow)o;

        if (HideLocalCheck.IsChecked == true && IsLocalOnly(r))
            return false;

        if (OnlyEstablishedCheck.IsChecked == true && r.State != "ESTABLISHED")
            return false;

        string q = FilterBox.Text?.Trim() ?? "";
        if (q.Length > 0)
        {
            string hay = $"{r.Process} {r.Pid} {r.Protocol} {r.LocalEndpoint} {r.RemoteAddress} {r.Host} {r.RemotePort} {r.State}";
            if (hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private static bool IsLocalOnly(ConnectionRow r)
    {
        if (r.RemotePort == 0) return true;
        return r.RemoteAddress is "" or "0.0.0.0" or "::" or "127.0.0.1" or "::1";
    }

    private static string FormatEndpoint(IPAddress addr, int port)
        => addr.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{addr}]:{port}"
            : $"{addr}:{port}";

    private static bool Confirm(string message)
        => MessageBox.Show(message, "Traffic Tracker — Onay",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private bool EnsureElevated()
    {
        if (_elevated) return true;
        MessageBox.Show(
            "Engelleme için uygulamanın yönetici olarak çalışması gerekir.\n" +
            "Lütfen kapatıp yönetici olarak yeniden başlatın.",
            "Traffic Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool TrySafe(Action action, string errorTitle)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{errorTitle}: {ex.Message}", "Traffic Tracker",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // --- UI olaylari ---

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

    private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalCombo.SelectedItem is ComboBoxItem item && item.Tag is int seconds)
            _timer.Interval = TimeSpan.FromSeconds(seconds);
    }
}
