# Değişiklik günlüğü

Sürüm notlarının depodaki kopyası. Ayrıntılı sürümler: [v5.1.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v5.1.0) · [v5.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v5.0.0) · [v4.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v4.0.0) · [v3.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v3.0.0) · [v2.5.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v2.5.0)

## Arayüz 5.1 — SAC-güvenli mod (deneysel), imzalama kancası

- **SAC-güvenli mod (deneysel)**: imzasız ikili çalıştırmadan Discord'u açan yol. `tools/warpac.ps1` Discord'a özel bir PAC yazar, `HKCU` WinINET `AutoConfigURL`'i yedekleyip ayarlar ve parçalayıcı proxy'yi **imzalı `powershell.exe` içinde** barındırır (`tools/warphost.ps1` + `Add-Type`). sing-box, TUN ve yönetici hakkı yok; IP gizlenmez. `--sac start|status|stop` veya Ayarlar → Genel anahtarı. Doğrulanan: proxy üzerinden `/api/v9/gateway` **200**, günlükte `CONNECT discord.com:443` (DoH), kapatınca port/PAC/kayıt temiz. Discord'un PAC davranışı henüz ölçülmedi.
- **`warp-frag.exe` imzalama kancası**: `Invoke-WarpSign`; `WARPCORD_SIGN_CERT` (+ `_TIMESTAMP`, `_PASSWORD`) tanımlıysa proxy derlenince imzalanır.
- **Erişilebilirlik**: düğme ve alanlara `AutomationProperties` adları/yardım metinleri.
- **`--selftest` 72 kontrol** (bayt biçimleme, komut temizleme eklendi).
- **Düzeltme**: `ScriptHost.Run` artık arka planda kalan alt süreç yüzünden kilitlenmiyor.

### Bilinen sınırlar
- SAC-güvenli mod deneyseldir; Discord kurulu + SAC açık makinede ölçülmeli.
- `sing-box.exe` imzalanamadığı için SAC açıkken WARP modu çalışmaz.
## Arayüz 5.0 — güven, otomasyon ve tanı

v5 "yeni ekran" değil, güvenilirlik sürümü: testler, CI, imza görünürlüğü, kurulum ve daha iyi tanı.

- **`--selftest`**: 65 saf mantık kontrolü (JSON, sürüm, i18n, uç sıralama, gömülü motor, işlem/CLI, log). Pencere/tünel gerekmez.
- **GitHub Actions CI**: her push/PR'da derle → selftest → smoke → artifact; `v*` etiketinde exe release'e eklenir. README'de rozet.
- **İmza görünürlüğü**: exe'nin Authenticode durumu PE güvenlik dizininden okunur; Hakkında ve `--smoke` gösterir. `gui\build.ps1 -Certificate` ile imzalama + doğrulama hazır.
- **Doğrulanmış güncelleme**: sürüm denetimi release asset'ini bulur, "İndir" ile indirir, GitHub SHA-256 özetiyle doğrular (kendini değiştirme yok).
- **Bu makineye kur / kaldır**: Başlat menüsü kısayolu (yönetici gerekmez), `--install` / `--uninstall-app`; "Windows açılışında tepside başla".
- **Ses (UDP) testi**: STUN binding isteğiyle ses yolu ölçülür (engelliyse amber).
- **Trafik sayacı**: `experimental.clash_api` (127.0.0.1:9099) üzerinden canlı ↑/↓ bayt.
- **Ek süreçler (deneysel)**: `install.ps1 -ExtraApps` + `extra-apps.json`; Discord dışı süreçler de tünele alınır (Gelişmiş panelde alan).
- **Adaptör DNS'i ve IPv6** satırları; pencere boyutu workarea'ya kırpılır.
- **Kısayollar**: F5, Ctrl+,; log sekmeleri için erişilebilirlik adları.
- **Dokümanlar**: `docs/SIGNING.md` (ücretsiz imza yolları, maliyetler, SAC gerçeği), `docs/SAC-SAFE.md` (imzasız ikili çalıştırmayan modun tasarımı), `docs/GUI.md` (kullanıcı rehberi).

### Bilinen sınırlar
- **SAC-güvenli mod** tasarım aşamasında: uygulaması Discord kurulu + SAC açık bir test makinesi bekliyor.
- `sing-box.exe` üçüncü taraf olduğu için imzalanamaz → SAC açıkken WARP modu, imzalı arayüze rağmen çalışmaz.
- **Ek süreçler** ve **trafik sayacı** deneyseldir; ek süreçler yalnızca `process_name` eşleşmesiyle çalışır.
## Arayüz 4.0 — tepsi, günlük sekmeleri, hazırlık ve uç araçları

- **Tepsi simgesi**: durum rengi (yeşil/amber/kırmızı/mavi), sağ tık menüsü (*aç / onar / çıkış*), tünel düşünce bildirim; küçültünce tepsiye iner.
- **Günlük sekmeleri**: İşlem / Tünel (`tunnel.log`) / Onarım (`heal.log`), renk kodlu ve canlı; boş sekme için açıklama.
- **Ayarlar penceresi** (dişli): dil Türkçe/İngilizce, tepsi-bildirim-otomatik onarım anahtarları, taşınabilir mod bilgisi.
- **Hazırlık denetimi**: `tools/warpreadiness.ps1` → SAC, WDAC/UMCI, Defender/ASR, CFA, politika, MOTW; satır satır sonuç ve uyarı.
- **Uç karşılaştırma**: `tools/warpendpoints.ps1` her adayı Discord API'sine gerçek istekle dener; tablo + *en iyisini kullan* / *seçileni kullan*.
- **Sürüm denetimi** (GitHub API) ve **tanı paketi** (`tani.ps1 -Bundle` → zip'i açar).
- **Discord'u yeniden başlat**: süreçleri kapatıp `Update.exe --processStart Discord.exe` ile açar.
- **Ağ değişti uyarısı**: `NetworkChange` olayı → "Yeniden Bağlan" şeridi.
- **Otomatik onarım**: sorun görüldüğünde (3 dakika soğuma ile) onarımı kendisi tetikler.
- **API gecikme grafiği**: son 30 ölçümün mini çizgisi.
- **Taşınabilir mod**: exe yanında `portable.txt` → ayarlar ve motor exe'nin yanında.
- Motor yardımcıları gömülü sete eklendi (10 dosya); exe 541 KB. Doğrulama: tarama (8/8), `--render`/`--settings`/`--prompt`, canlı açılış + tepsi, İngilizce arayüz render'ı.

## Arayüz 1.3 — tek dosya

- Motor (install / status / onar / tanı / kaldır + DPI kaynağı) exe'ye gömüldü; ilk açılışta `%LOCALAPPDATA%\WarpCord\engine\<sürüm>\` altına açılır (~163 KB, exe 466 KB).
- Kaynak seçimi: yanındaki repo → `state.json` → gömülü motor. `--engine` gömülü motoru zorlar.
- Tek dosya modunda *Elle (WarpCord.bat)* kapalı; arayüz "tek dosya modu" der.
- Doğrulama: boş klasörde tek exe ile `--smoke`, `--exec status` ve tam kurulum (24 sn, çıkış 0).

## Arayüz 1.2 — Smart App Control + imzalama

- `tools/sac-check.bat` + `sac-state.ps1`: SAC durumunu okur, `WarpCord.bat` kurulumdan önce uyarır (E/H). Arayüz açıkken üstte şerit gösterir.
- `gui/build.ps1 -Certificate <parmak izi|pfx>`: signtool ile imzalar, zaman damgası basar, doğrular.

## Arayüz 1.1 — kurulum yöntemi seçimi

- İlk açılışta *Uygulama ile* / *Elle (WarpCord.bat)* sorulur ve hatırlanır; Gelişmiş'ten değiştirilir.
- Canlı durum paneli, tek düğme, renkli günlük, iptal, isteğe bağlı UAC.
- Komut satırı: `--smoke`, `--exec`, `--render`, `--prompt`.

## v3.0.0 — temiz Windows 11'de tek tık

- MOTW, GPO (AllSigned/Restricted), SmartScreen, Smart App Control, Defender/ASR, Controlled Folder Access, WDAC/UMCI, proxy ve ConstrainedLanguage engelleri için tespit ve çözüm.
- `tani.bat` (ölçüm + öneri, `-Bundle` ile zip), kurulum hatalarında ipucu, `kaldir.bat` istisnaları geri alır.

## v2.5.0 — koruyucu

- `WarpCordHeal` 5 dakikada bir tüneli denetler, bozulanı onarır; `heal.log`, `heal-state.json`, elle `onar.bat`.

## v2.0.0 — tek tık + DPI motoru

- Python bağımlılığı kalktı; `wintun.dll` kurulur, uç/port otomatik seçilir, WARP engelliyse TLS parçalama motoru devreye girer.
- DNS yalnızca gerektiğinde değişir, başarısız kurulumda geri alınır; `durum.bat` ve `kaldir.bat`; AMD64 + ARM64.
