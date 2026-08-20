# Üçüncü Taraf Bildirimleri (Third-Party Notices)

Traffic Tracker aşağıdaki üçüncü taraf bileşenlerini kullanır. Bu bileşenler
kendi lisanslarına tabidir; MIT lisansı yalnızca bu projenin kendi kaynak
koduna uygulanır.

---

## WinDivert

- **Kullanım:** Program başına gelen/giden hız sınırlama (paket yakalama/enjeksiyon).
- **Dahil edilen dosyalar:** `libs/windivert/WinDivert.dll`, `libs/windivert/WinDivert64.sys`
- **Sürüm:** 2.2.2
- **Telif hakkı:** Copyright (C) basil00 ve katkıda bulunanlar
- **Proje:** https://github.com/basil00/WinDivert
- **Lisans:** GNU Lesser General Public License v3 (LGPLv3) **veya** GNU General
  Public License v2 (GPLv2) — kullanıcının seçimine göre. Bu proje bileşeni
  **LGPLv3** koşulları altında kullanır.
- **Lisans metni:** `libs/windivert/LICENSE-WinDivert.txt`

WinDivert kütüphanesine **dinamik** olarak (ayrı bir `WinDivert.dll` üzerinden)
bağlanılır. LGPLv3 gereği, bu DLL kullanıcı tarafından uyumlu başka bir sürümle
değiştirilebilir. WinDivert'in kaynak kodu yukarıdaki proje adresinden edinilebilir.

> Not: WinDivert ayrı bir **ticari** lisans altında da sunulur. Bu proje ticari
> lisansı kullanmaz; yalnızca yukarıdaki açık kaynak (LGPLv3) koşullarına dayanır.

---

## Microsoft.Diagnostics.Tracing.TraceEvent

- **Kullanım:** Program başına bant genişliği ölçümü (ETW olayları).
- **Dahil edilme:** NuGet paketi olarak referans verilir (kaynakta ikili dosya yok).
- **Telif hakkı:** Copyright (c) Microsoft Corporation
- **Proje:** https://github.com/microsoft/perfview
- **Lisans:** MIT

---

## .NET / Windows API'leri

Windows Filtering Platform (Güvenlik Duvarı), `iphlpapi`, `sc`, `reg`, ETW ve
QoS gibi bileşenler işletim sisteminin parçasıdır ve Microsoft'a aittir.
