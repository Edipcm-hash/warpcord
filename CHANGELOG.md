# Değişiklik günlüğü

Sürüm notlarının depodaki kopyası. Ayrıntılı sürümler: [v4.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v4.0.0) · [v3.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v3.0.0) · [v2.5.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v2.5.0)

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
