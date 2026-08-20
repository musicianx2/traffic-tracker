# Traffic Tracker

Bilgisayarındaki tüm ağ trafiğini izlemek, yönetmek ve istediğini kesmek için
kişisel bir Windows aracı. Ek olarak Windows Update / Store güncellemelerini
kalıcı kontrol altına alma modülü hedefleniyor.

## Durum: Faz 1 (İzleme) + Faz 2 (Kesme) — tamamlandı

### Bağlantılar sekmesi (izleme)
- Hangi **program** (PID → ad) → **nereye** (IP + ters DNS host adı) → hangi **port**
- TCP + UDP, IPv4 + IPv6
- TCP durumu (ESTABLISHED, LISTEN, TIME-WAIT, …)
- Filtre kutusu (program/IP/host/port/durum), yerel-dinleme gizleme, sadece
  ESTABLISHED, yenileme aralığı ve duraklat.
- **Sağ tık → engelle:** bir satırda sağ tıklayıp *programı*, *IP'yi* veya
  *portu* tek tıkla engelleyebilirsin.

### Kurallar sekmesi (kesme)
- Tanımladığın engelleme kuralları listelenir; **Aktif** kutusuyla anında
  aç/kapat, sağ tık → sil.
- Kurallar **Windows Güvenlik Duvarı'na** yazılır (`INetFwPolicy2` COM) ve
  **kalıcıdır** — bilgisayar yeniden başlasa da geçerli kalır.
- Kurallar `%APPDATA%\TrafficTracker\rules.json` içinde de saklanır.

> Faz 2 için uygulama **yönetici** olarak çalışır (güvenlik duvarı yazma izni).
> Kısayola sağ tık → "Yönetici olarak çalıştır" ya da UAC onayı ile açılır.

### Geçmiş sekmesi
- Yaptığın her kural işlemi (ekleme / silme / açma / kapatma) **zaman damgasıyla**
  listelenir; `%APPDATA%\TrafficTracker\history.json`'da saklanır.
- **Geri al:** bir işlemi seçip tersine çevir (silineni geri ekler, ekleneni siler,
  açılanı kapatır). Yanlış bir kuraldan kolayca dönmek için.

### Windows Update sekmesi (Faz 3)
- `wuauserv`, `UsoSvc`, `WaaSMedicSvc`, `dosvc` servislerinin başlangıç türü ve
  çalışma durumunu gösterir.
- **🔒 Kilitle:** servisleri devre dışı bırakır + durdurur, otomatik güncellemeyi
  (NoAutoUpdate) ve Store otomatik indirmeyi kapatır.
- **🔓 Kilidi Aç:** servisleri varsayılana döndürür, politikaları kaldırır — sonra
  güncellemeyi elle yapabilirsin.
- Yerleşik `sc` / `reg` ile çalışır ([Services/WindowsUpdateManager.cs](Services/WindowsUpdateManager.cs)).
  `WaaSMedicSvc` korumalı olduğundan "erişim reddi" verebilir; zaten devre dışıysa
  sorun değildir.

## Teknoloji

- **.NET 8 + WPF** (native masaüstü arayüz)
- Bağlantılar: `iphlpapi.dll` → `GetExtendedTcpTable` / `GetExtendedUdpTable`
  (owner-PID) — [Native/IpHelper.cs](Native/IpHelper.cs)
- Process eşleme: [Services/ProcessResolver.cs](Services/ProcessResolver.cs)
- Ters DNS (arka plan, önbellekli): [Services/DnsResolver.cs](Services/DnsResolver.cs)
- Program yolu (PID→exe): [Native/ProcessPath.cs](Native/ProcessPath.cs)
- Engelleme (Windows Güvenlik Duvarı COM): [Services/FirewallManager.cs](Services/FirewallManager.cs)
- Kural modeli / kalıcılık: [Models/BlockRule.cs](Models/BlockRule.cs),
  [Services/RuleStore.cs](Services/RuleStore.cs)

## Çalıştırma

```powershell
dotnet build TrafficTracker.csproj -c Debug
.\bin\Debug\net8.0-windows\TrafficTracker.exe
```

### Faz 4 — Cila (kısmen tamamlandı)
- **Canlı hız grafiği:** Bağlantılar sekmesinin üstünde anlık indirme (↓) /
  yükleme (↑) hızı + son ~2 dakikanın sparkline grafiği
  ([Services/NetSpeedService.cs](Services/NetSpeedService.cs)).
- **Sistem tepsisi (tray):** pencereyi küçültünce tepsiye iner, arka planda
  izlemeye devam eder. Tepsi simgesine çift tık → geri aç; sağ tık → Çıkış.
- **Bilgi panelleri (info):** Kurallar ve Windows Update sekmelerinde açılır
  "ℹ Nasıl kullanılır / ne işe yarar" açıklamaları.

### Program başına bant genişliği + Ayarlar
- **↓/s · ↑/s kolonları:** her programın (PID) anlık indirme/yükleme hızı,
  ETW ile ölçülür ([Services/BandwidthMonitor.cs](Services/BandwidthMonitor.cs) —
  Resource Monitor'un kullandığı yöntem). Başlığa tıklayıp yükseğe göre sırala.
- **Ayarlar sekmesi:** bir eşik (KB/s) belirle; eşiği aşan programın satırı
  kırmızı vurgulanır. `%APPDATA%\TrafficTracker\settings.json`.
- **Windows Update — tek servis:** listede bir servise **sağ tıkla** →
  devre dışı bırak+durdur / durdur / başlat / Manuel / Otomatik.

### Program hız sınırlama (deneysel · WinDivert)
- **Gelen + giden** hız sınırı, program başına. Bağlantılar'da bir satıra sağ tık →
  "🐢 Hız sınırı koy", sonra **Ayarlar** sekmesinden ↓/↑ KB/s değerlerini düzenle
  (0 = o yönde sınır yok).
- İmzalı WinDivert 2.2.2 sürücüsü `libs/windivert/` içinde paketlenir, exe yanına
  kopyalanır. Motor: [Services/ThrottleEngine.cs](Services/ThrottleEngine.cs)
  (token-bucket + paket yeniden enjeksiyonu).
- **Güvenlik:** sürücü yalnızca bir sınır ekleyip "etkinleştir" dediğinde açılır;
  kapatınca / uygulama kapanınca / hata olunca anında serbest bırakılır ve ağ
  normale döner. Yalnızca sınırlı programın uç-noktaları şekillendirilir.
- ⚠ Deneysel: canlı paket şekillendirme burada test edilmedi; kendi makinende
  test et. Kapatmak için Ayarlar'daki onay kutusunu kaldırman yeterli.

## Kalan işler (isteğe bağlı Faz 4+)

- Zaman-bazlı kural otomasyonu (ör. gece 00–06 arası şu trafiği engelle).
- `WaaSMedicSvc` için registry sahiplik devralma (korumalı servisi tam kilitleme).
- Hız sınırlama motoru için gerçek-dünya testi ve ince ayar.

## Lisans

Bu projenin kendi kaynak kodu **MIT** lisansı altındadır — bkz. [LICENSE](LICENSE).

Proje bazı üçüncü taraf bileşenleri kullanır ve bunlar kendi lisanslarına tabidir
(kâr amacı güdülmez):

- **WinDivert** (`libs/windivert/`) — **LGPLv3 / GPLv2**, © basil00. Bu projede
  LGPLv3 koşulları altında, DLL'e dinamik bağlanarak kullanılır. Lisans metni:
  [libs/windivert/LICENSE-WinDivert.txt](libs/windivert/LICENSE-WinDivert.txt).
- **Microsoft.Diagnostics.Tracing.TraceEvent** — MIT, © Microsoft (NuGet).

Ayrıntılar için [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
