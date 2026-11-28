# İmzalama (kod imzası) — v5 notları

WarpCord imzasız dağıtılıyor. Bu iki yerde sorun çıkarır:

1. **Smart App Control (SAC)** — Windows 11'de açıksa imzasız/repütasyonsuz ikili **hiç çalışmaz**, uygulama bazlı izin listesi yoktur.
2. **SmartScreen** — ilk indirmede "Windows protected your PC" uyarısı; imza + repütasyonla kaybolur.

## Ücretsiz yollar

| Yol | Maliyet | Kim imzalar | Notlar |
|---|---|---|---|
| **SignPath Foundation** | Ücretsiz | SignPath (CA sertifikası) | OSI lisansı (MIT ✓), aktif bakım, yayınlanmış sürüm, MFA ve "yalnızca kendi ikilin" koşulları. Başvuru onayı gerekir: <https://signpath.org> |
| **Microsoft Store** | Ücretsiz hesap | Store | Yalnızca MSIX; TUN sürücüsü + yönetici gerektiren bu araç için pratik değil |
| **Kendinden imzalı** | Ücretsiz | sen | SAC'ı ikna etmez; yalnızca sertifikayı güvenilenlere eklediğin makinelerde SmartScreen/WDAC tarafında işe yarar |
| Azure Trusted Signing | ~$10/ay | Azure | CI'a en kolay; kimlik/şirket doğrulaması ve bölge desteği şart |
| OV sertifika (CA) | $300–500/yıl | CA | Token/HSM zorunlu; CI için ayrıca bulut HSM gerekir |

> SAC **ECC sertifikaları kabul etmez**; RSA kullanın.

## Bizim build ile imzalama

```bat
gui\build.ps1 -Certificate <parmak izi>                  :: sertifika deposundan
gui\build.ps1 -Certificate c:\yol\warpcord.pfx -PfxPassword ****
```

- `dist\WarpCord.exe` `signtool` ile imzalanır, zaman damgası basılır ve `Get-AuthenticodeSignature` ile **doğrulanır**.
- `signtool.exe` yoksa build net bir hata verir (Windows SDK / Visual Studio gerekir).
- Uygulama, Hakkında sekmesinde **imza durumunu** gösterir (`imzalı · CN=...` / `imzasız`). Bu bilgi `--smoke` çıktısında da vardır.

## CI (GitHub Actions)

`.github/workflows/build.yml` hazır: derle → `--selftest` → `--smoke` → artifact → etikette Release'e exe ekleme.
SignPath onayından sonra imza adımını açmak için şu secret'ları tanımla:

```
SIGNPATH_API_TOKEN  SIGNPATH_ORG_ID  SIGNPATH_PROJECT  SIGNPATH_POLICY
```

SignPath istemcisi (GitHub Action) `artifact-configuration-slug: exe` ile tek dosyayı imzalar; `warp-frag.exe` de aynı yapılandırmaya eklenebilir (kurulum sırasında derlendiği için ayrıca ele alınmalı).

## SAC'lı makinelerde gerçek durum

- `WarpCord.exe` imzalanınca **arayüz** her makinede açılır.
- Tünel için `sing-box.exe` (üçüncü taraf, imzasız) çalışmak zorunda; SAC onu engelliyorsa WARP modu çalışmaz.
- v5'te **SAC-güvenli DPI yolu** araştırılıyor: kendi proxy'miz + PAC ile sing-box'a hiç ihtiyaç duymayan, yalnızca bizim (imzalanabilir) ikilimizi kullanan mod.
- Şimdilik kesin çözüm: SAC'ı kapatmak (geri alınamaz) — uygulama bunu uyarı olarak gösteriyor.

## Doğrulama komutları

```powershell
Get-AuthenticodeSignature .\dist\WarpCord.exe | Format-List Status, SignerCertificate
signtool verify /pa /v .\dist\WarpCord.exe
```
