Türkçe | **[English](README.md)**

# WarpCord

**Discord'a özel split tunnel — tek tıkla, Python'suz.** VPS yok, hesap yok, tüm cihazı VPN'e sokmak yok: sadece Discord trafiği tünelden çıkar, geri kalan her şey doğrudan gitmeye devam eder.

> **v2 — ne değişti?** Tek tık kurulum, Python bağımlılığının kalkması, `wintun.dll` kurulumu, WARP uçlarının otomatik seçimi ve **Superonline gibi WARP'ı tamamen kapatan hatlar için DPI motoru**. Ayrıntı: [Sürüm 2](#sürüm-2--ne-değişti).

> **v2.5 — koruyucu:** `WarpCordHeal` görevi tüneli 5 dakikada bir denetler, bozulan parçayı kendisi onarır ve her hatayı `C:\ProgramData\DiscordTunnel\heal.log` dosyasına yazar. Ayrıntı: [Sürüm 2.5](#sürüm-25--kendi-kendini-onaran-koruyucu).

Windows 11 (build 26200) + sing-box 1.14.4 üzerinde, Discord'un ISP seviyesinde **DNS zehirlenmesi + SNI filtresi** ile engellendiği bir ağda test edildi. v2'nin DPI motoru ayrıca aynı engel profiline sahip TTNet hattında canlı olarak doğrulandı.

---

## Neden iki motor var?

Discord Türkiye'de engelli ve engel aslında iki katmanlı:

1. **DNS zehirlenmesi** — `discord.com` ISP'nin blok sayfasına yönlendiriliyor.
2. **SNI filtresi** — ClientHello içinde "discord" geçen TLS bağlantısı anında resetleniyor.

**WARP motoru** ikisini birden çözer: DNS tünelin içinde DoH ile yanıtlanır, TLS de tünelin içinden çıkar, dışarıda sadece WireGuard paketleri görünür.

Ama bazı hatlarda — özellikle **Superonline** — Cloudflare WARP'ın kendisi engelli. O zaman tünel kurmak mümkün olmaz. Bu yüzden v2, engeli doğrudan bölen ikinci bir motor getiriyor: **TLS ClientHello'yu birden fazla TLS kaydına bölmek**. RFC 8446 bir handshake mesajının birden çok kayda yayılmasına açıkça izin verir; sunucu sorunsuz birleştirir, ama DPI "discord.com"u tek bir kayıtta göremez. Bu, GoodbyeDPI/zapret'in yaptığı işin sürücüsüz, sadece Discord'a kapsanmış ve tek dosyalık hâli.

Kurulum ikisini de bilir, hangisi çalışıyorsa onu seçer:

| Mod | Ne zaman | IP'n değişir mi? | Ses |
|---|---|---|---|
| `warp` | WARP hattında çalışıyorsa | Evet, Discord trafiği Cloudflare'den çıkar | Evet, UDP tünellenir |
| `dpi` | WARP engelliyse (Superonline) | **Hayır**, sadece engel aşılır | Genelde evet, ses UDP'si doğrudan gider |

## Hızlı başlangıç — tek tık

1. Arşivi bir klasöre çıkar (Masaüstü olabilir).
2. **`WarpCord.bat`** dosyasına çift tıkla, UAC istemini onayla. (Çift tıkta önce 5 saniyelik kedi selamı çıkar; komut satırından argümanla çalıştırırsan atlanır.)
3. Bekle. Kurulum kendi başına: sing-box + `wintun.dll` indirir, ücretsiz WARP cihazı kaydeder, uçları tek tek dener, çalışan modu seçer, görevleri kurar ve **Discord API'sine gerçek bir istekle** doğrular.
4. Discord'u tamamen kapat (tepsi simgesinden de) ve yeniden aç.

Kontrol: **`durum.bat`** &nbsp;•&nbsp; Kaldırma: **`kaldir.bat`**

Gereksinimler: Windows 10/11 **64-bit** (AMD64 veya ARM64), yönetici hakkı (TUN sürücüsü ister), internet. **Python gerekmiyor.**

## Uyumluluk — hangi Windows, hangi bağlantı?

Kablolu/kablosuz ayrımı yoktur: ikisi de aynı TUN + yönlendirme yolunu kullanır. Fark yalnızca adaptör adı ve DNS'in yazıldığı arayüzdür; ikisi de desteklenir.

| Ortam | Durum |
|---|---|
| Windows 10 (1809+) / Windows 11, 64-bit | ✅ desteklenir ve test edilir |
| Windows 11 ARM64 | ✅ (arm64 ikili + arm64 `wintun.dll`) |
| **Kablolu Ethernet** | ✅ |
| **Wi-Fi** | ✅ |
| USB tethering / telefon hotspot'u | ✅ (WARP engelliyse DPI motoru) |
| Router arkasında PPPoE (Superonline fiber tipi) | ✅ PC'de normal Ethernet/Wi-Fi görünür |
| PC'de doğrudan PPPoE bağlantısı | ⚠️ dener; adaptör adı boşsa arayüz indeksiyle yazar |
| IPv6 kapalı sistemler | ⚠️ WARP modu çalışmaz → otomatik DPI modu (IP gizlenmez) |
| Başka bir VPN açık | ⚠️ çakışır; WarpCord'u kullanırken diğer VPN'i kapatın |
| Captive portal (otel/kafe giriş sayfası) | ⚠️ önce portalı geçin; portal açıkken kurulum uyarı verir |
| 32-bit Windows | ❌ sing-box'ın 32-bit Windows sürümü yok |
| Windows 7 / 8.1 | ❌ PowerShell 5.1 + sing-box 1.14 gereksinimleri karşılanmaz |
| Yönetici olmayan kullanıcı | ❌ UAC yükseltmesi gerekir |
| Hiç internet yok | ❌ (kurulum bunu erken tespit edip söyler) |

**Ağ değiştirirseniz** (ör. Wi-Fi'den kabloya geçiş, yeni bir modem): DNS ayarı kurulumda trafiği taşıyan adaptöre, her adaptör için ayrı yedekle yazılır. Discord açılmazsa `WarpCord.bat` dosyasını tekrar çalıştırın — yeni adaptörü de yedekleyip düzeltir.

## Sürüm 2 — ne değişti

| Eskiden | Şimdi |
|---|---|
| `register.bat` + `install.bat`, Python şart | Tek `WarpCord.bat`; WARP kaydı PowerShell'de (RFC 7748 X25519, C# derlemesi) |
| `wintun.dll` hiç kurulmuyordu (TUN çoğu makinede açılmaz) | `wintun.dll` indirilir ve sing-box'ın yanına konur |
| Sabit uç `162.159.192.1:2408` | Cloudflare'in verdiği uç + 2408/500/1701/4500 portları + yedek anycast adresleri + varsa IPv6; en hızlı çalışan seçilir |
| WARP engelliyse çıkmaz sokak | DPI motoru otomatik devreye girer |
| DNS her kurulumda değiştirilirdi | Sadece ISP DNS'i zehirliyorsa değiştirilir; başarısız kurulumda otomatik geri alınır |
| Tüm DNS Cloudflare'e giderdi | DNS sadece gerektiğinde değiştirilir; zehirli cevaplar DoH ile tamamen devre dışı kalır |
| `uninstall.bat` dışında kontrol yok | `durum.bat` canlı sağlık kontrolü, `kaldir.bat` temiz kaldırma |
| Sadece AMD64 | AMD64 + ARM64, TLS 1.2 zorlaması, BOM'suz JSON, Türkçe karakterli yollar |

## Sürüm 2.5 — kendi kendini onaran koruyucu

v2.5 ile kurulum sadece kurmuyor, **çalışır durumda tutuyor**. Her 5 dakikada bir çalışan `WarpCordHeal` görevi tüneli denetler ve bozulan parçayı kendisi onarır:

| Kontrol | Bozuksa yapılan |
|---|---|
| `sing-box.exe` / `wintun.dll` var mı | GitHub / wintun.net üzerinden yeniden indirilir |
| Zamanlanmış görev var mı, etkin mi | Yeniden oluşturulur / etkinleştirilir |
| `config.json` geçerli mi (`sing-box check`) | `account.json` + kayıtlı state'ten yeniden üretilir |
| sing-box süreci çalışıyor mu | Tünel yeniden başlatılır (koşu başına en fazla 2 kez) |
| TUN adaptörü ayakta mı | Yeniden başlatılır, 20 sn'ye kadar beklenir |
| DPI modunda proxy ve portu dinliyor mu | Proxy görevi yeniden başlatılır |
| Discord DNS'i zehirli mi | DNS 1.1.1.1'e geri alınır (yedekle) |
| Discord API yanıt veriyor mu | Son çare tünel yeniden başlatılır |

- Her kontrol, bulgu ve onarım **`C:\ProgramData\DiscordTunnel\heal.log`** dosyasına yazılır; dosya 2 MB'ı geçince `heal.log.old` olarak döner.
- Son koşunun özeti `heal-state.json` dosyasında tutulur, `durum.bat` bunu **Onarim** satırında gösterir.
- Elle onarım: **`onar.bat`** (veya `durum.bat -Repair`). Çıkış kodları: `0` sağlıklı, `1` onarıldı, `2` kurtarılamadı, `3` yönetici yetkisi yok.
- Koruyucu görevi SYSTEM olarak koşar; `durum.bat` yönetici istemez ama onarım için `onar.bat` UAC ister.

## Sürüm 3.0 — temiz Windows 11'de tek tık

Orijinal/temiz Windows 11'de kurulumu kıran şey WarpCord'un kodu değil, Windows'un kendi korumalarıydı. Ölçülen engeller ve v3'ün cevabı:

| Engel | Ne yapıyor | v3 çözümü |
|---|---|---|
| **MOTW + RemoteSigned** | Zip'ten çıkan `.ps1` "not digitally signed" hatasıyla hiç yüklenmez | `WarpCord.bat` açılışta tüm dosyalardan `Zone.Identifier` akışını siler ve `Unblock-File` çalıştırır; `app/*.ps1` de kendi klasörünü temizler |
| **GPO: AllSigned / Restricted** | `-ExecutionPolicy Bypass` bile `.ps1` dosyasını yükletmez | Launcher politikayı algılar, betiği belleğe alıp `[scriptblock]::Create` ile çalıştırır (`-RepoRoot` ile) |
| **SmartScreen** | "Windows protected your PC" | Dosyalar unblock edildiği için MOTW kalkar; imza olmadığından ilk indirmede bir kez "Yine de çalıştır" gerekebilir |
| **Smart App Control** | İmzasız `sing-box.exe` / `warp-frag.exe` çalışmaz | Erken tespit; `tani.bat` ve `install.log` tek çözümü söyler (SAC kapatmak geri dönüşsüzdür) |
| **Defender / ASR 5BEB7EFE** | İndirilen/çıkarılan içeriği engeller veya karantinaya alır | MOTW temizliği + kurulum/repo klasörü ve `sing-box.exe`, `warp-frag.exe` için istisna |
| **Controlled Folder Access** | Korumalı klasör yazımlarını engeller | CFA açıksa ikililer izin listesine eklenir |
| **WDAC / UMCI (user-mode)** | İmzasız betik ve ikili çalışmaz | Tespit + net mesaj; otomatik aşılamaz, kullanıcı politikayı kaldırmalı |
| **Proxy / TLS denetimi** | GitHub indirmesi kopar | Kalıcı cache (`cache\`) + `bin\` klasöründen yerel kopya + TLS probe teşhisi |
| **ConstrainedLanguage** | Betikler hiç çalışmaz | Erken tespit, çıkış kodu 9 ve açıklama |
| **PSModulePath kirliliği** (PowerShell 7 Store kurulumu) | PS 5.1 yanlış modülü yükler; `Get-ExecutionPolicy`, Defender ve görev cmdletleri "module could not be loaded" ile patlar | Süreç içinde `PSModulePath` kanonik 5.1 yollarına sıfırlanır |

Ek olarak:

- **`tani.bat`** her şeyi ölçer (sürüm, politika, SAC/WDAC, Defender/ASR/CFA, MOTW, proxy, DNS, GitHub TLS, görevler, loglar) ve **`tani.bat -Bundle`** ile paylaşılabilir zip üretir.
- Kurulum herhangi bir adımda düşerse **ipucu** basar (`Get-WarpErrorHint`) ve `tani.bat`'a yönlendirir.
- `kaldir.bat`, eklenen Defender/CFA istisnalarını da geri alır.
- `app/`, `tools/`, `docs/` klasörleriyle kök dizin sadeleştirildi; tek tık girişi hâlâ `WarpCord.bat`.

## Engel gerçekte neydi

Her katmanı ayrı ayrı test ettim (`tools/diagnose.py`):

| Katman | Sonuç |
|---|---|
| DNS | `discord.com` → `195.175.254.2` (ISP'nin blok sayfası). Diğer her şey normal çözülüyordu. |
| 1.1.1.1 / 8.8.8.8 / 9.9.9.9'e düz DNS | **Reset.** Yani "DNS'i değiştir" tek başına hiçbir işe yaramıyor. |
| DoH (443) / DoT (853) | Çalıştı. Şifreli DNS temiz geçiyordu. |
| IP bloğu | **Yok.** Discord'un gerçek Cloudflare IP'lerine TCP 443, 14–40 ms'de bağlanıyordu. |
| MITM | **Yok.** Normal siteler gerçek sertifikalarını sunuyordu. |
| SNI filtresi | **Engel buydu.** Aynı IP: `SNI=example.com` → TLS sorunsuz. `SNI=discord.com` → anında reset. |
| WARP (UDP 2408/500/1701/4500) | **Değişken.** Bazı uç/portlar yanıt vermiyor, ama 500 portu üzerinden 150–800 ms'de el sıkışma kurulabiliyor. Test makinesinde IPv6 kapalı olduğu için WARP modu yine de kullanılamadı (aşağıdaki test notuna bakın). |

## Nasıl çalışıyor

```
  Discord.exe ─────┐
  *.discord.com ───┤  TUN (auto_route) ── sing-box ──┬── warp  : WireGuard ──▶ Cloudflare WARP ──▶ Discord
                   │                                 ├── frag  : 127.0.0.1:1081 ──▶ TLS kayıt parçalama ──▶ Discord
                   │                                 └── direct ─────────────▶ diğer her şey
```

- **sing-box** bir TUN cihazı (`WarpCord`) oluşturur ve varsayılan rotayı devralır.
- **Yönlendirme kuralları** akış bazında karar verir: alan adı Discord'a aitse **veya** süreç Discord'sa → `warp`/`frag`, değilse → `direct` (`final`).
- **DNS** sing-box'a kaçırılır ve DoH (1.1.1.1) ile yanıtlanır; ISP'nin zehirli cevapları Windows'a hiç ulaşmaz. Ağın DNS'i zaten temizse adaptör ayarına hiç dokunulmaz ve çözümleme ISS çözücüsünde kalır (bkz. SSS).
- **`warp` modunda** çıkış ücretsiz bir WARP cihazıdır.
- **`dpi` modunda** Discord'un TCP akışları `warp-frag.exe` adlı küçük bir yerel HTTP CONNECT proxy'sine gider. Proxy, ClientHello'yu SNI'ın tam ortasından iki/üç TLS kaydına böler. DoH isteği bile bu proxy üzerinden gider, böylece `cloudflare-dns.com` gibi isimler filtrelense de DNS çalışır.

### WARP kısmı

WARP, Cloudflare'in edge'ine açılan bir WireGuard tüneli. Resmî istemci kurulum ve hesap istiyor; "cihaz kaydet" API'si istemiyor.

`tools/warpcore.ps1` her şeyi PowerShell ile yapıyor:

1. **X25519** anahtar çifti üretir (RFC 7748 Montgomery ladder, C# ile derlenir — test vektörleriyle doğrulandı).
2. Açık anahtarla `POST https://api.cloudflareclient.com/v0a5641/reg` çağırır (alternatif API sürümleri ve gerekirse DPI proxy üzerinden yedekli).
3. Peer açık anahtarını, atanan adresi ve `client_id`'yi alır.
4. `base64(client_id)`'nin ilk 3 byte'ından WireGuard `reserved` alanını türetir.
5. Çalışmaya hazır sing-box `config.json` yazar.

### Uç otomatik seçimi (Superonline için önemli)

Cloudflare'in verdiği uç tek seçenek değil. Kurulum sırayla dener: verilen uç → 500 / 1701 / 4500 portları → yedek anycast adresleri → (varsa) IPv6 uçları. Her adayı gerçekten **Discord API'sine istek atarak** sınar ve en hızlı çalışanı yazar. Hiçbiri çalışmazsa DPI moduna geçer.

### Seçici kısmı

```jsonc
"route": {
  "rules": [
    { "action": "sniff" },                            // alan adını TLS SNI'dan oku
    { "protocol": "dns", "action": "hijack-dns" },    // sistem DNS'ini devral
    { "domain_suffix": ["discord.com", "discord.gg", ...], "outbound": "warp" },
    { "process_name": ["Discord.exe", "Update.exe", ...], "outbound": "warp" },
    { "process_path_regex": ["(?i).*[\\\\/]Discord[\\\\/].*"], "outbound": "warp" }
  ],
  "final": "direct"                                   // eşleşmeyen trafik: asla tünellenmez
}
```

İki kural ailesi farklı şeyleri yakalar: alan adı kuralı Discord'la konuşan her şeyi (tarayıcı dahil), süreç kuralı ise IP ile ve **UDP** ile giden akışları — sesli kanalların çalışmasını sağlayan şey bu.

## Komut satırı seçenekleri

```bat
WarpCord.bat                     :: otomatik: dene, çalışan modu seç (önerilen)
WarpCord.bat -Mode warp          :: sadece WARP; çalışmazsa hata verir
WarpCord.bat -Mode dpi           :: sadece DPI motoru (WARP kaydı denemez)
WarpCord.bat -Force              :: yeni bir ücretsiz WARP cihazı kaydet
WarpCord.bat -Endpoint 162.159.192.1:2408   :: ucu elle sabitle
WarpCord.bat -KeepDns            :: sistem DNS'ine hiç dokunma
WarpCord.bat -SkipProbe          :: uç denemesini atla, kayıtlı ucu kullan
durum.bat                        :: canlı sağlık kontrolü
durum.bat -Restart               :: görevleri yeniden başlat
durum.bat -Repair                :: onarımı elle çalıştır (UAC)
onar.bat                         :: koruyucuyu çalıştır: denetle, onar, heal.log'a yaz
tani.bat                         :: teşhis: neden çalışmadı? rapor + öneriler
tani.bat -Bundle                 :: teşhis raporunu zip olarak üret
kaldir.bat                       :: her şeyi kaldır, DNS'i geri al
```

## Çalıştığını doğrula

```powershell
# 162.159.x.x dönmeli — ISP'nin blok IP'si değil
Resolve-DnsName discord.com -Type A

# Önbellekte blok sayfası cevabı olmamalı (boş çıktı iyi durum)
Get-DnsClientCache | Where-Object { $_.Data -eq '195.175.254.2' }

# Canlı log: Discord akışları warp/frag, gerisi direct
Get-Content C:\ProgramData\DiscordTunnel\tunnel.log -Tail 30
```

Sağlıklı log (warp modu):

```
router: found process path: C:\...\Discord\app-1.0.9267\Discord.exe
inbound connection to 34.126.226.51:443
endpoint/wireguard[warp]: outbound connection to 34.126.226.51:443
```

Sağlıklı log (dpi modu):

```
inbound connection to discord.com:443
outbound/http[frag]: outbound connection to discord.com:443
```

ve aynı dosyada Discord dışı trafik:

```
outbound/direct[direct]: outbound connection to 104.20.23.154:443
```

Bir kontrol daha — API gateway adresini dönmeli:

```powershell
(Invoke-WebRequest https://discord.com/api/v9/gateway -UseBasicParsing).Content
# {"url": "wss://gateway.discord.gg"}
```

## Dosyalar

| Dosya | Görev |
|---|---|
| `WarpCord.bat` | **Tek tık**: kur, onar, mod seç. UAC ile kendini yükseltir, MOTW temizler, gerekirse belleğe alıp çalıştırır |
| `tani.bat` / `tools/tani.ps1` | **Tanılama**: neden çalışmadığını söyler (politika, Defender, SAC, ağ, loglar) ve rapor üretir |
| `tools/splash.bat` | Çift tıkta 5 saniyelik ASCII kedi animasyonu (silersen çıkmaz) |
| `app/install.ps1` | Kurulumun tamamı: indir, kaydet, uç seç, kur, doğrula, gerekirse geri al |
| `durum.bat` / `app/status.ps1` | Canlı sağlık kontrolü (yönetici gerekmez) |
| `onar.bat` / `tools/warpheal.ps1` | **Koruyucu**: her 5 dakikada bir tüneli denetler, bozulanı onarır, `heal.log`'a yazar |
| `kaldir.bat` / `app/uninstall.ps1` | Görevleri, güvenlik duvarı kurallarını, dosyaları siler; DNS'i ve Defender istisnalarını geri alır |
| `tools/register.bat` / `tools/warp-register.ps1` | Sadece WARP kaydı + config üretimi (PowerShell) |
| `tools/warpcore.ps1` | Motor: X25519, kayıt, uç seçimi, config üretimi, DNS yardımcıları |
| `tools/warpfrag/WarpFrag.cs` | DPI motoru: TLS kayıt parçalayan yerel proxy (bağımlılıksız) |
| `tools/warpfrag.ps1` | Proxy'yi derle/test et (`-Build`, `-Test`, `-Run`) |
| `tools/diagnose.py` | *(isteğe bağlı, Python 3)* Ağında engelin gerçekte ne yaptığını ölçer (DNS, SNI, sertifika) |
| `docs/config.example.json` / `docs/config.example.dpi.json` | Üretilen config'lerin şablonları |

İlk çalıştırmada sing-box (~33 MB) ve `wintun.dll` indirilir. İndirme istemiyorsan bunları `bin\sing-box.exe` ve `bin\wintun.dll` olarak koyabilirsin; kurulum önce oraya bakar.

Kurulumun sisteme dokunduğu tek yerler: `C:\ProgramData\DiscordTunnel` klasörü (loglar ve `heal-state.json` dahil), zamanlanmış görevler (`DiscordOnlyTunnel`, gerekirse `WarpCordFrag`, `WarpCordHeal`), çıkış güvenlik duvarı izinleri ve **yalnızca DNS zehirliyse** adaptör DNS'i (yedeklenir).

## Sınırlar ve SSS

**Superonline'da çalışır mı?** v2 tam bunun için yapıldı. `WarpCord.bat` önce WARP'ı dener; Superonline WARP'ı kapatıyorsa otomatik olarak DPI motoruna geçer ve Discord yine açılır. DPI modu IP'ni gizlemez (bu bir tünel değil, DPI atlatma), WARP modu gizler.

**Bu bir VPN mi?** Hayır. Sadece Discord akışları tünele/proxy'ye girer. Diğer her şeyde IP'n ve rotan değişmez.

**DNS'imi değiştiriyor mu?** Sadece gerekiyorsa. Windows tüm tanımlı çözücülere paralel sorduğu için, ISP çözücüsü Discord için blok sayfası IP'si döndürmeye devam ederse Windows iki cevabı da önbelleğe alır. Böyle bir durumda adaptör `1.1.1.1`e alınır, bu paketler tünele girer ve sing-box onları DoH ile yanıtlar. Ağın DNS'i zaten temizse hiç dokunulmaz; `kaldir.bat` (veya başarısız kurulum) DHCP'yi geri getirir.

**IPv6 trafiği ne oluyor?** TUN bilinçli olarak yalnızca IPv4 taşır; IPv6 akışları tünelin dışında kalır ("bağlantına dokunma" felsefesi). DNS'in IPv6 tarafı da Cloudflare'e yönlendirilir ki zehirli cevap gelmesin; Discord IPv6'yı denerse kısa sürede IPv4'e düşer. IPv6'yı tamamen tünellemek istemiyorsanız bu davranış zaten istediğiniz şeydir.

**Ağ değiştirirsem (Wi-Fi → Ethernet)?** DNS ayarı, kurulum sırasında trafiği taşıyan adaptöre uygulanır ve her adaptör için ayrı yedek tutulur. Yeni bir ağa geçtikten sonra Discord açılmıyorsa `WarpCord.bat` dosyasını tekrar çalıştırın: yeni adaptörü de yedekleyip DNS'i düzeltir. `kaldir.bat` değiştirdiği tüm adaptörleri geri alır.

**Ses çalışıyor mu?** WARP modunda evet (UDP tünellenir). DPI modunda ses UDP'si doğrudan gider; ISP Discord'un UDP medyasını ayrıca engellemiyorsa çalışır. Ses sorunluysa WARP'ı olan bir hat/çıkış kullan.

**IPv6 kapalıysa?** Kurulum bunu anlar. Windows'ta IPv6 kapatıldığında (`DisabledComponents` / `ms_tcpip6` kapalı) sing-box'ın WireGuard ucu IPv6 soketini adaptöre bağlayamıyor ve WARP modu çalışmıyor; bu durumda araç otomatik olarak DPI motoruna geçer ve Discord yine açılır (IP gizlenmez). WARP modunu istiyorsanız IPv6'yı açıp `WarpCord.bat` dosyasını yeniden çalıştırın.

**Ne kadar hızlı?** WARP modunda 20–100 Mbps tipik; Discord için fazlasıyla yeter, indirmeler zaten tüneli kullanmıyor. DPI modu ek gecikme olarak ~1–5 ms getirir.

**Bozulursa?** Koruyucu genelde 5 dakika içinde kendisi düzeltir; ne yaptığını `heal.log` dosyasında görebilirsin. Hemen müdahale için `onar.bat` çalıştır. O da çözmezse `kaldir.bat` çalıştır: görevler, güvenlik duvarı kuralları ve dosyalar silinir; DNS geri alınır; rotalar TUN cihazıyla birlikte kaybolur. Kurulum başarısız olursa da DNS otomatik geri alınır — internetin kopmaz.

**WARP sonradan engellenirse?** `WarpCord.bat` dosyasını yeniden çalıştır: uçları yeniden dener, olmuyorsa DPI moduna geçer. Tamamen yeni bir cihaz için `-Force`.

**Antivirüs/güvenlik uyarısı?** TUN sürücüsü ve yönetici hakkı ister; `wintun.dll` resmî WireGuard imzalı sürücüdür. Derlenen `warp-frag.exe` imzasızdır (kaynak `tools/warpfrag/WarpFrag.cs`, tek dosya).

**Config'te özel anahtar var mı?** Evet, `C:\ProgramData\DiscordTunnel\config.json` ve `account.json` WARP cihazının özel anahtarını içerir. ACL ile sadece SYSTEM/Yöneticiler/sen erişebilirsin; bu dosyaları paylaşma, repoya koyma.

**Neden VPN değil?** Çünkü VPN *her şeyi* yönlendirir: CS2 ping'i ikiye katlanır, indirmeler başka ülkeden geçer, yerel servisler bozulur. Bu araç varsayılan olarak hiçbir şeyi yönlendirmez, sadece Discord'u dahil eder.

## Test notu

Geliştirme hattında (TTNet profili) `discord.com` → `195.175.254.2` ve `SNI=discord.com` → reset doğrulandı. WARP el sıkışması bu hatta **kısmen** çalışıyor: 2408 portu bazen zaman aşımına düşerken 500 portu 150–800 ms'de bağlanıyor — uç/port otomatik seçimi tam bu yüzden var. Ancak test makinesinde IPv6 kapatıldığı için (`DisabledComponents=255`) sing-box'ın WireGuard ucu adaptör bağlamasını yapamıyor; kurulum bunu tespit edip otomatik olarak DPI motoruna geçti ve Discord API'si ~340 ms'de gerçek cevabı döndürdü. IPv6 açık bir makinede WARP yolu, aynı uç seçimi ve doğrulama ile çalışır. TUN sürücüsü, görevler ve adaptör DNS'i yönetici hakkı olan bir Windows makinesinde son kullanıcı doğrulaması ister.

## Sorumluluk reddi

İşim ve arkadaşlarım için ihtiyaç duyduğum bir servise erişmek üzere kendi kullanımım için geliştirdim. Kendi riskinle ve bulunduğun yerin yasalarına uygun kullan. Discord veya Cloudflare ile bağlantılı değildir; WARP ücretsiz bir Cloudflare servisidir ve çalışma garantisi yoktur.

MIT lisanslı.
