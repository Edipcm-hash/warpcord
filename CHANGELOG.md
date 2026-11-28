# Değişiklik günlüğü

Sürüm notlarının depodaki kopyası. Ayrıntılı sürümler: [v3.0.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v3.0.0) · [v2.5.0](https://github.com/Edipcm-hash/warpcord/releases/tag/v2.5.0)

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
