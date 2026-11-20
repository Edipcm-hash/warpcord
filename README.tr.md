Türkçe | **[English](README.md)**

# WarpCord

**Cloudflare WARP üzerinden Discord'a özel split tunnel.** VPS yok, hesap yok, tüm cihazı VPN'e sokmak yok — sadece Discord trafiği tünelden çıkar, geri kalan her şey doğrudan gitmeye devam eder.

Windows 11 (build 26200) + sing-box 1.14.4 üzerinde, Discord'un ISP seviyesinde **DNS zehirlenmesi + SNI filtresi** ile engellendiği bir ağda test edildi.

---

## Neden yaptım

Discord Türkiye'de engellendi. Her rehber "VPN kur" diyordu; ama VPN *her şeyi* yönlendirir: CS2 pingim ikiye katlanır, indirmeler başka ülkeden geçer, yerel servisler bozulur. Ben tam tersini istiyordum: **bağlantıma dokunma, sadece Discord'u tünelle.**

Bu yüzden tahmin yürütmek yerine bir akşam boyunca engelin gerçekte ne yaptığını ölçtüm. Engel göründüğünden çok daha zayıf çıktı. Bu depo o ölçümün sonucu.

## Engel gerçekte neydi

Her katmanı ayrı ayrı test ettim (`tools/diagnose.py`):

| Katman | Sonuç |
|---|---|
| DNS | `discord.com` → `195.175.254.2` (ISP'nin blok sayfası). Diğer her şey normal çözülüyordu. |
| 1.1.1.1 / 8.8.8.8 / 9.9.9.9'e düz DNS | **Reset.** Yani "DNS'i değiştir" hiçbir işe yaramıyor — ISP tüm UDP/53 paketlerini karıştırıyor. |
| DoH (443) / DoT (853) | Çalıştı. Şifreli DNS temiz geçiyordu. |
| IP bloğu | **Yok.** Discord'un gerçek Cloudflare IP'lerine TCP 443, 14–40 ms'de bağlanıyordu. |
| MITM | **Yok.** Normal siteler gerçek sertifikalarını sunuyordu (Cloudflare, Google, Sectigo). |
| SNI filtresi | **Engel buydu.** Aynı IP: `SNI=example.com` → TLS 1.3 sorunsuz. `SNI=discord.com` → anında TCP reset. |

Blok sayfasının kendisi kendinden imzalı sertifika (`CN=localhost.localdomain`) döndürüyordu — yani gerçek bir araya girme değil, ISP yönlendirmesi.

Özetle: **zehirlenmiş DNS + ClientHello içinde Discord geçen bağlantıyı kesen bir kutu.** IP kara deliği yok, derin MITM yok.

## Neleri eledim

- **Hosts dosyası / 1.1.1.1** — port 53 kaçırılıyor; üstelik SNI reset'i bağlantıyı yine öldürürdü.
- **Tam VPN** — sorunu çözer ama tüm trafiği yönlendirir. Yanlış araç.
- **GoodbyeDPI / Zapret (ClientHello parçalama)** — çalıştığını doğruladım (ClientHello'yu 1–3 byte'lık parçalara bölmek DPI'ı atlatıyor), ama sistem genelinde paket cerrahisi, IP'yi gizlemiyor ve tünel değil. Yedek plan olarak kaldı.
- **VPS + VLESS/Reality** — en sağlam seçenek ama aylık maliyet.
- **Genel proxy'ler** — güvenilmez; Discord trafiğimi tanımadığım birine emanet etmem.

Cloudflare WARP tam orta noktaydı: **ücretsiz, hesapsız, gerçek tünel, UDP destekli** (yani ses çalışır). Tek eksiği seçici olmamasıydı; onu da ben ekledim.

## Nasıl çalışıyor

```
  Discord.exe ─────┐
                   ├── tun0 (TUN, auto_route) ── sing-box ──┬── WARP (WireGuard) ──▶ Discord
  *.discord.com ───┘                                        └── direct ────────────▶ diğer her şey
```

- **sing-box** bir TUN cihazı oluşturup varsayılan rotayı devralır.
- **Yönlendirme kuralları** akış bazında karar verir: süreç adı *veya* alan adı Discord'a aitse → `warp`, değilse → `direct`.
- **DNS** sing-box'a kaçırılır; sadece Discord alan adları DoH (1.1.1.1) ile çözülür, gerisi normal sistem çözücüsünü kullanır. Böylece DNS zehirlenmesi yalnızca Discord için devre dışı kalır.
- **Tünelin çıkışı ücretsiz bir WARP cihazıdır.** Zincirde ücretli hiçbir sunucu yok.

### WARP kısmı (işin eğlenceli tarafı)

WARP, Cloudflare'in edge'ine açılan bir WireGuard tüneli. Resmî istemci kurulum ve hesap istiyor; "cihaz kaydet" API'si istemiyor.

`tools/warp-register.py` her şeyi Python standart kütüphanesiyle yapıyor:

1. **X25519** anahtar çifti üretir (RFC 7748, sıfır bağımlılık).
2. Açık anahtarla `POST https://api.cloudflareclient.com/v0a5641/reg` çağırır.
3. Peer açık anahtarını, atanan adresi (`172.16.0.2`) ve `client_id`'yi alır.
4. `base64(client_id)`'nin ilk 3 byte'ından WireGuard `reserved` alanını türetir.
5. Çalışmaya hazır sing-box `config.json` yazar.

### Seçici kısım

Config'in can alıcı satırları:

```jsonc
"route": {
  "rules": [
    { "action": "sniff" },                                // alan adını TLS SNI'dan oku
    { "protocol": "dns", "action": "hijack-dns" },         // sistem DNS'ini devral
    { "domain_suffix": ["discord.com", "discord.gg", "discordapp.com",
                        "discordapp.net", "discord.media", "discordstatus.com"],
      "outbound": "warp" },
    { "process_name": ["Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe",
                       "DiscordDevelopment.exe", "Update.exe"],
      "outbound": "warp" }
  ],
  "final": "direct"                                        // eşleşmeyen trafik: asla tünellenmez
}
```

Tek kural değil iki kural, çünkü farklı şeyleri yakalıyorlar: alan adı kuralı Discord'la konuşan her şeyi (tarayıcı dahil) yakalar, süreç kuralı ise IP ile ve **UDP** ile giden akışları — sesli kanalların çalışmasını sağlayan şey bu.

## Hızlı başlangıç

Gereksinimler: Windows 10/11, yönetici hakkı (TUN sürücüsü ister), internet, tek seferlik WARP kaydı için Python 3.8+.

```bat
:: 1. Ücretsiz WARP cihazı kaydet ve config.json üret
register.bat

:: 2. Tüneli kur (UAC ile kendini yükseltir)
install.bat
```

`install.bat` gerisini yapar:

- sing-box'ı indirir (1.14.4'e sabitli), `bin\` içinde yoksa
- her şeyi `C:\ProgramData\DiscordTunnel` altına kopyalar
- binary için giden firewall kuralı ekler
- `DiscordOnlyTunnel` görevini oluşturur (SYSTEM, açılışta, hata durumunda yeniden başlar)
- başlatır ve sonucu doğrular

Sonra Discord'u tamamen kapat (tepsi simgesinden de) ve yeniden aç.

## Çalıştığını doğrula

```powershell
# 162.159.x.x dönmeli — ISP'nin blok IP'si değil
Resolve-DnsName discord.com -Type A

# Canlı log: Discord akışları wireguard[warp], gerisi direct
Get-Content C:\ProgramData\DiscordTunnel\tunnel.log -Tail 30
```

Sağlıklı log şöyle görünür:

```
router: found process path: C:\...\Discord\app-1.0.9267\Discord.exe
inbound connection to 34.126.226.51:443
endpoint/wireguard[warp]: outbound connection to 34.126.226.51:443
```

ve aynı dosyada Discord dışı trafik:

```
inbound connection to 104.20.23.154:443
outbound/direct[direct]: outbound connection to 104.20.23.154:443
```

Bir kontrol daha — tünelden Discord API'si gateway adresini dönmeli:

```powershell
(Invoke-WebRequest https://discord.com/api/v9/gateway -UseBasicParsing).Content
# {"url": "wss://gateway.discord.gg"}
```

## Dosyalar

| Dosya | Görev |
|---|---|
| `install.bat` / `install.ps1` | Kendini yükselten kurulum: indir, kaydet, görev kur, doğrula |
| `uninstall.bat` / `uninstall.ps1` | Görevi, firewall kuralını ve dosyaları kaldırır |
| `register.bat` | Sadece WARP kaydını çalıştırır (`config.json` üretir) |
| `tools/warp-register.py` | WARP cihaz kaydı + sing-box config üretici (sadece stdlib) |
| `tools/diagnose.py` | *Senin* ağında engelin gerçekte ne yaptığını ölçer (DNS, SNI, sertifika). Tünel kapalıyken çalıştır |
| `config.example.json` | Üretilen config'in şablonu (anahtarlar yer tutucu) |

## Sınırlar ve SSS

**Bu bir VPN mi?** Hayır. Sadece Discord akışları tünele girer. Diğer her şeyde IP'n değişmez.

**Kurulum neden DNS'imi değiştiriyor?** Çünkü Windows *tüm* tanımlı çözücülere paralel soruyor: tünel doğru cevabı verirken ISP çözücüsü Discord için blok sayfası IP'sini döndürmeye devam ediyor ve Windows iki cevabı da önbelleğe alıyor. Adaptörü `1.1.1.1`e yönlendirince bu DNS paketleri tünele giriyor ve sing-box onları DoH ile yanıtlıyor. Dokunulan tek sistem ayarı bu; `uninstall.bat` DHCP'yi geri getiriyor.

**Ses çalışıyor mu?** Evet — WARP UDP taşır ve süreç kuralı Discord'un UDP akışlarını yakalar.

**Ne kadar hızlı?** WARP'a bağlı, tipik 20–100 Mbps. Discord için fazlasıyla yeter; indirmeler zaten tüneli kullanmıyor.

**Bozulursa?** `uninstall.bat` çalıştır. Görev, firewall kuralı ve dosyalar silinir; rotalar TUN cihazıyla birlikte kaybolur.

**WARP sonradan engellenirse?** `register.bat` ile yeni cihaz kaydı al. Eğer *endpoint* engellenirse WARP endpoint'ini kendi VPS'indeki VLESS/Reality ile değiştir — yönlendirme kuralları aynı kalır, sadece `endpoints` bloğu değişir.

**Discord'u VPN'den hariç tutmak varken neden bu?** Çünkü o ters problem: çoğu VPN istemcisi varsayılan olarak her şeyi yönlendirir ve "hariç tut" modları kabadır. Bu araç varsayılan olarak hiçbir şeyi yönlendirmez, sadece Discord'u dahil eder.

## Sorumluluk reddi

İşim ve arkadaşlarım için ihtiyaç duyduğum bir servise erişmek üzere kendi kullanımım için geliştirdim. Kendi riskinle ve bulunduğun yerin yasalarına uygun kullan. Discord veya Cloudflare ile bağlantılı değildir; WARP ücretsiz bir Cloudflare servisidir ve çalışma garantisi yoktur.

MIT lisanslı.
