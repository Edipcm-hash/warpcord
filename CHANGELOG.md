# Değişiklik günlüğü

Bu dosya sürüm notlarının depodaki kopyasıdır. Ayrıntılı, biçimlendirilmiş sürümler GitHub Releases sayfalarındadır:

- [v3.0.0 — temiz Windows 11'de tek tık + tanılama](https://github.com/Edipcm-hash/warpcord/releases/tag/v3.0.0)
- [v2.5.0 — kendi kendini onaran koruyucu](https://github.com/Edipcm-hash/warpcord/releases/tag/v2.5.0)

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
