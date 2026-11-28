# Değişiklik günlüğü

Bu dosya sürüm notlarının depodaki kopyasıdır. Ayrıntılı, biçimlendirilmiş sürümler GitHub Releases sayfalarındadır:

- [v3.0.0 — temiz Windows 11'de tek tık + tanılama](https://github.com/Edipcm-hash/warpcord/releases/tag/v3.0.0)
- [v2.5.0 — kendi kendini onaran koruyucu](https://github.com/Edipcm-hash/warpcord/releases/tag/v2.5.0)

## Arayüz 1.3 — tek dosya: motor exe'nin içinde

`WarpCord.exe` artık **kendi başına** çalışıyor; yanında `app\` ve `tools\` klasörleri olmadan da kurulum yapıyor.

- **Gömülü motor**: `app\install.ps1`, `status.ps1`, `uninstall.ps1`, `tools\warpcore.ps1`, `warpheal.ps1`, `tani.ps1`, `warpfrag.ps1` ve `tools\warpfrag\WarpFrag.cs` gömülü kaynak olarak taşınır (~163 KB, exe 466 KB). İlk kullanımda `%LOCALAPPDATA%\WarpCord\engine\<sürüm>\` altına bayt bayt açılır (`.engine` sürüm damgası; eski sürüm klasörleri budanır).
- **Kaynak seçimi**: exe'nin yanındaki/üstündeki gerçek repo > `state.json`'daki kayıtlı klasör > gömülü motor. `--engine` gömülü motoru zorlar; durum çubuğu hangisinin kullanıldığını yazar ("gömülü motor · …").
- **Tek dosya modunda arayüz**: *Elle (WarpCord.bat)* seçeneği kapanır, panel "tek dosya modu" der, ana düğme ölü bir "Elle Kur" etiketi göstermez ([görünüm](docs/ui-standalone.png)).
- **Doğrulama**: boş bir klasöre tek başına kopyalanan exe ile `--smoke` 8/8, `--exec status` çıkış 0 ve **tam kurulum** (`--exec install`) 24,2 sn'de çıkış 0; çıkarılan dosyalar kaynakla SHA-256 düzeyinde aynı.

## Arayüz 1.2 — Smart App Control uyarısı + imzalama desteği

Windows 11'in **Akıllı Uygulama Denetimi** (Smart App Control) açıkken imzasız ve itibarı bilinmeyen hiçbir ikili çalışmıyor ve uygulama bazlı izin listesi yok; bu yüzden imzasız `WarpCord.exe` hiç başlamıyor, `sing-box.exe` de engelleniyor. Artık bu durum sessiz bir hata değil:

- **`tools/sac-check.bat` + `tools/sac-state.ps1`**: SAC durumunu okur (`0` kapalı, `1` açık, `2` değerlendirme). `WarpCord.bat` açılışta çağırır: açıksa kuruluma girmeden uyarır, ayarın tam yolunu yazar ve **E/H** sorar (yükseltilmiş kopyada sessiz). `WARPCORD_SAC_FORCE` ile dallar test edilebilir.
- **Arayüzde şerit**: SAC açık/değerlendirme modundaysa üstte amber uyarı ve *Nasıl kapatılır?* düğmesi; adımları ve "geri alınamaz" notunu gösterir.
- **`gui\build.ps1 -Certificate <parmak izi|.pfx>`**: signtool ile Authenticode imzalama + zaman damgası + doğrulama. Dağıtımda tek kalıcı çözüm; sertifika Microsoft kök programındaki bir CA'dan gelmeli, ECC kabul edilmez.
- `--smoke` çıktısına SAC durumu eklendi; `tani.bat` mesajı artık `WarpCord.exe`'yi de anıyor.

## Arayüz 1.1 — masaüstü uygulaması + kurulum yöntemi seçimi

Komut satırı araçlarının üzerine, aynı motoru tek pencereden yöneten bir WPF uygulaması eklendi: `dist\WarpCord.exe`. Kurulum paketi, Python, .NET SDK ya da ek çalışma zamanı gerekmez — Windows 10/11 ile gelen .NET Framework 4.8 üzerinde çalışan tek dosyadır (~300 KB).

- **Canlı durum paneli**: mod, uç, TUN adaptörü, `sing-box`/proxy süreçleri, son onarım durumu, DNS, Discord API gecikmesi ve dış IP. Süreç/dosya durumu 3 saniyede, ağ kontrolleri 20 saniyede bir yenilenir; ağ kontrolü UI'yı kilitlemez.
- **Bağlama duyarlı tek düğme**: kurulu değilse *Kur ve Bağlan*, sorun varsa *Onar*, sağlıklıysa *Yeniden Bağlan*.
- **Betik çıktısı pencereye akar**: Durum/Onar/Tanı düğmeleri mevcut PowerShell betiklerini çalıştırır, satır satır renklendirir (OK yeşil, hata kırmızı, adım camgöbeği, bölüm mor) ve işlem sırasında *İptal* süreç ağacını durdurur.
- **Yönetici akışı**: arayüz yönetici olmadan açılır ve durumu gösterir; kurulum/onarım/kaldırma istendiğinde kendini `--run` ile UAC üzerinden yeniden başlatır. Tek örnek koruması, yeniden başlatma öncesi bırakılır.
- **Gelişmiş panel**: motor seçimi (Otomatik/WARP/DPI), kurulum yöntemi (Uygulama ile / Elle), yeni WARP cihazı, DNS'e dokunmama, elle uç (biçim doğrulanır), kurulumu kaldırma (onay diyaloglu).
- **İki kurulum yolu**: kurulu olmayan makinede ilk açılışta "Uygulama ile kur" / "Elle kur — WarpCord.bat" seçimi sorulur ve hatırlanır. "Elle" seçiliyken ana düğme **Elle Kur (WarpCord.bat)** olur, panelde *Klasik kurulumu başlat* düğmesi çıkar; `WarpCord.bat` yönetici olarak kendi konsol penceresinde açılır, arayüz açık kalıp durumu izler. Komut satırından `WarpCord.exe --exec manual` aynı işi yapar. Klasik `WarpCord.bat` / `durum.bat` / `onar.bat` / `kaldir.bat` yolu hiç değişmeden durur.
- **Arayüz komut satırı**: `--smoke` (XAML/logo/betik/ayar + canlı durum duman testi, çıkış kodu), `--exec <işlem>` (arayüzsüz çalıştır; aynı `Runner` yolunu kullanır), `--render p.png [--demo] [--state broken|notinstalled|dpi]` (arayüzü PNG'ye çizer), `--prompt p.png` (kurulum yöntemi penceresini çizer).
- **Logo**: portal halkası içinden geçen "W" kurdelesi. `tools/make-logo.py` her boyutu ayrı çizer (16–256 px), PNG-şıkıştırmalı çok boyutlu `.ico` ve SVG üretir; exe simgesi olarak gömülür.
- **Derleme**: `gui\build.ps1` — yalnızca `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` kullanır (`/noconfig`, C# 5), XAML ve görselleri gömülü kaynak yapar, `-Shortcut` ile masaüstü kısayolu, `-Smoke` ile duman testi çalıştırır.

## v3.0.0 — Temiz Windows 11'de tek tık + tanılama

Temiz/orijinal Windows 11'de kurulumu kıran şey kod değil, Windows'un kendi korumalarıydı. v3 bu duvarları kaldırır ve geri kalanı için teşhis sistemi getirir.

### Aşılan engeller

| Engel | Ne yapıyordu | v3 çözümü |
|---|---|---|
| MOTW + RemoteSigned | Zip'ten çıkan `.ps1` "not digitally signed" ile yüklenmiyordu | `WarpCord.bat` `Zone.Identifier`'ı siler, `Unblock-File` çalıştırır; `app/*.ps1` kendi klasörünü de temizler |
| GPO: AllSigned / Restricted | `-ExecutionPolicy Bypass` bile `.ps1` yükletmiyordu | Launcher politikayı algılar, betiği belleğe alıp `[scriptblock]::Create` ile çalıştırır |
| PSModulePath kirliliği (PowerShell 7 Store) | PS 5.1 yanlış modülü yükler; `Get-ExecutionPolicy`, Defender ve görev cmdletleri "module could not be loaded" ile patlar | Süreç içinde `PSModulePath` kanonik 5.1 yollarına sıfırlanır |
| SmartScreen | "Windows protected your PC" | MOTW kalktığı için uyarı çıkmaz; imza olmadığından ilk indirmede bir kez "Yine de çalıştır" gerekebilir |
| Smart App Control | İmzasız `sing-box.exe` / `warp-frag.exe` çalışmaz | Erken tespit; `tani.bat` ve `install.log` tek çözümü söyler (SAC kapatmak geri dönüşsüzdür) |
| Defender / ASR 5BEB7EFE | İndirilen/çıkarılan içeriği engeller veya karantinaya alır | MOTW temizliği + kurulum/repo klasörü ve ikililer için Defender istisnası |
| Controlled Folder Access | Korumalı klasör yazımlarını engeller | CFA açıksa ikililer izin listesine eklenir |
| WDAC / UMCI (user-mode) | İmzasız betik ve ikili çalışmaz | Tespit + net mesaj; otomatik aşılamaz |
| Proxy / TLS denetimi | GitHub indirmesi kopar | Kalıcı `cache\` + `bin\` yerel kopya + TLS probe teşhisi |
| ConstrainedLanguage | Betikler hiç çalışmaz | Erken tespit, çıkış kodu 9 ve açıklama |

### Yenilikler

- **`tani.bat`**: sürüm, ExecutionPolicy, SAC/WDAC, Defender/ASR/CFA, MOTW, proxy, DNS, GitHub TLS, görevler, loglar ve son Defender olaylarını tek raporda toplar; sonuç + öneri basar. `tani.bat -Bundle` paylaşılabilir zip üretir.
- **Hata ipuçları**: kurulum düşerse `Get-WarpErrorHint` eyleme dönük Türkçe öneri yazar.
- **Bağımlılık sağlamlığı**: indirilen `sing-box.exe` / `wintun.dll` kalıcı `cache\` klasörüne SHA256 manifestiyle yazılır; ağ olmasa da sonraki kurulumlar buradan ilerler. `bin\` hâlâ ilk tercih.
- **Tam yetki hazırlığı**: kurulum yönetici olarak yükselir; Defender istisnalarını ve CFA izinlerini ekler, hepsini `install.log`'a yazar. `kaldir.bat` geri alır.
- **Depo düzeni**: `app/`, `tools/`, `docs/`; kökte sadece launcher'lar, README'ler ve LICENSE.

### Test edilenler

- MOTW'lu dosyalar + RemoteSigned: önce "not digitally signed", v3 sonrası kurulum başarılı.
- `cmd` üzerinden `WarpCord.bat` (kirli PSModulePath): önce çıkış 9, düzeltme sonrası çıkış 0 ve tünel çalışıyor.
- `sing-box` süreci öldürülünce koruyucu 25 sn içinde onardı; silinen `config.json` yeniden üretildi.
- **VMware Workstation 26 + resmî Windows 11 Pro imajı** (varsayılan ayarlar): masaüstünden `WarpCord.bat` ile kurulum sorunsuz tamamlandı (kullanıcı testi, 2026-11-27).

### Bilinen sınırlar

Smart App Control ve user-mode WDAC otomatik aşılamaz; bu bir kullanıcı/politika kararıdır. İmza gerektiren kurumsal hatlarda kod imzalama sertifikası gerekir.

## v2.5.0 — Kendi kendini onaran koruyucu

Kurulum artık sadece kurmuyor: `WarpCordHeal` görevi tüneli her 5 dakikada bir denetliyor ve bozulan parçayı kendisi onarıyor.

- **WarpCordHeal görevi** (SYSTEM, 5 dakikada bir, açılışta bir kez): süreç, TUN adaptörü, zamanlanmış görev, `config.json`, DNS ve Discord API kontrolü.
- **Otomatik onarım**: eksik `sing-box.exe` / `wintun.dll` yeniden indirilir; geçersiz config `account.json` + state'ten üretilir; süreç, TUN ve DPI proxy'si yeniden başlatılır (koşu başına en fazla 2 restart); DNS zehirlenirse 1.1.1.1'e geri alınır.
- **`heal.log`**: her kontrol/bulgu/onarım zaman damgasıyla `C:\ProgramData\DiscordTunnel\heal.log` dosyasına yazılır, 2 MB'ta `heal.log.old` olarak döner. Çökme anında `tunnel.log`'un son satırları kanıt olarak eklenir.
- **`heal-state.json`**: son koşunun özeti; `durum.bat` bunu **Onarim** satırında gösterir.
- **`onar.bat`** ve `durum.bat -Repair`: elle onarım. Çıkış kodları: `0` sağlıklı, `1` onarıldı, `2` kurtarılamadı, `3` yönetici yetkisi yok. Zamanlanmış koşu `-Quiet` ile onarımda da `0` döner.
- Kurulum, koruyucu betiklerini `C:\ProgramData\DiscordTunnel\tools\` altına kopyalar; repo taşınsa bile görev çalışır. `kaldir.bat` görevi kaldırır.

## v2.0.0 — Sıfırdan yeniden yazım

Tek tık kurulum, Python bağımlılığının kaldırılması, `wintun.dll` kurulumu, WARP uçlarının otomatik seçimi, Superonline gibi WARP'ı kapatan hatlar için DPI motoru ve DNS'in yalnızca gerektiğinde değiştirilmesi.
