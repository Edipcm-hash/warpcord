# SAC-güvenli mod (ücretsiz, imzasız ikili çalıştırmayan yol) — tasarım

Amaç: **Smart App Control açık** bir makinede hiçbir imzasız süreç çalıştırmadan Discord'u açmak.
Bugünkü iki engel:

| Süreç | İmzalı mı | SAC'lı makinede |
|---|---|---|
| `WarpCord.exe` (arayüz) | hayır | engellenir → uygulama açılmaz |
| `sing-box.exe` (TUN + yönlendirme) | hayır (üçüncü taraf) | engellenir → tünel kurulmaz |
| `warp-frag.exe` (DPI proxy) | hayır (bizim) | engellenir |
| `powershell.exe`, `cmd.exe` | **Microsoft** | çalışır |

## Tasarım A — PAC + in-process proxy (önerilen)

```
cmd.exe (imzalı)
  └─ powershell.exe (imzalı)
       └─ WarpFrag proxy (Add-Type ile belleğe derlenir, ayrı exe yok)
            · 127.0.0.1:1081 CONNECT proxy, ClientHello'yu TLS kayıtlarına böler
            · DNS'i DoH ile kendisi çözer (ISP zehirlenmesi devre dışı)
Windows WinINET (kullanıcı) 
  └─ AutoConfigURL = file:///.../WarpCord/proxy.pac
        · discord.com / discord.gg / cdn.discordapp.com / gateway → PROXY 127.0.0.1:1081
        · diğer her şey → DIRECT
Discord (Electron) → WinINET proxy ayarını kullanır → yalnızca Discord akışı proxy'den geçer
```

- **TUN yok, sing-box yok, yönetici hakkı yok.** Tüm süreçler imzalı; proxy kodu bellekte.
- IP gizlenmez (bilinen DPI modu davranışı), ses UDP'si proxy'ye girmez (doğrudan gider).
- Arayüz tarafı: SAC'lı makinede `WarpCord.exe` yine engellenir. Bu yüzden ya
  (a) SignPath ile imzalanmış exe dağıtılır, ya (b) minik bir PowerShell arayüzü (`--sac-safe` durum penceresi)
  eklenir, ya da (c) kullanıcıya SAC'ı kapatması söylenir.

## Tasarım B — her şeyi imzala

SignPath (ücretsiz) `WarpCord.exe` + `warp-frag.exe` imzalar; `sing-box.exe` üçüncü taraf olduğu için
imzalanamaz → SAC'lı makinede WARP modu yine çalışmaz. DPI modu için de sing-box'ın TUN'una ihtiyaç
duyulduğundan bu yol tek başına yetmez.

## Doğrulama planı (gerekli: Discord kurulu, SAC açık temiz Win11 VM)

1. `--sac-safe` başlat → PAC yazılır, proxy 1081 dinler, AutoConfigURL ayarlanır.
2. `Resolve-DnsName discord.com` → 195.175.254.2 (zehirlenmiş) olsa bile proxy DoH kullandığı için
   Discord bağlanır; kanıt: `tunnel.log` yerine proxy logunda `CONNECT discord.com:443`.
3. Discord: gateway (wss) + metin + ses; `Get-Content %APPDATA%\discord\logs\*.log` içinde ağ hataları.
4. Kapatma: `AutoConfigURL` silinir, proxy durur, WinINET eski hâline döner (yedeklenir).
5. SAC açıkken WarpCord.exe'nin engellenmesi: imzalı sürüm veya PowerShell arayüzü ile çözülür.

## Açık sorular
- Discord'un PAC desteği: Chromium WinINET `AutoConfigURL`'i okur; **ölçülmesi gerek**.
- Proxy üzerinden WSS: CONNECT tünellemesi sorunsuz olmalı; ölçülecek.
- Ses: UDP doğrudan; ISP UDP'yi engelliyorsa DPI modunda da bugünkü davranış aynı.
- PAC dosyası konumu ve `file://` izni; alternatif: `http://127.0.0.1:1082/proxy.pac` (proxy'nin kendisi servis eder).

## Karar
Test makinesi (Discord + SAC) sağlanınca Tasarım A `--sac-safe` deneysel bayrağı arkasında uygulanacak;
o zamana kadar ücretsiz ve kesin çözüm SAC'ı kapatmak (uygulama bunu uyarı olarak gösteriyor).
