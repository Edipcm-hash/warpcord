# WarpCord — temiz Windows 11 testi

Bu klasör, WarpCord'u **orijinal/temiz bir Windows 11** üzerinde otomatik olarak sınamak için gereken her şeyi içerir:

- `Test-CleanWindows.ps1` — **VM içinde** çalışır: dosyalara mark-of-the-web ekler, ExecutionPolicy'yi stock Windows varsayılanı olan `RemoteSigned` yapar, kurulumu sıfırdan çalıştırır ve 18 kontrolle sonucu doğrular.
- `New-TestVm.ps1` — **Hyper-V sunucusunda** çalışır: vTPM + Secure Boot'lu Gen2 VM kurar, `autounattend.xml` ile Windows 11'i sessizce kurar, repoyu kopyalar ve testi PowerShell Direct ile çalıştırır.
- `autounattend.xml` — katılımsız kurulum cevap dosyası (hesap: `Test` / `Test1234!`).

## Neden bu test?

Temiz Windows 11'de kurulumu kıran şey kod değil, Windows'un korumalarıdır: MOTW + RemoteSigned ("not digitally signed"), SmartScreen, Smart App Control, Defender/ASR, CFA, GPO ile zorlanan ExecutionPolicy ve kirli `PSModulePath`. Bu test tam olarak o koşulları üretir.

## Gereksinimler

- Windows 11 Pro/Enterprise + **Hyper-V rolü** (yeniden başlatma gerekir):
  ```powershell
  Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All
  Restart-Computer
  ```
- Resmî Windows 11 ISO'su. Lisans gerektirmeyen 90 günlük değerlendirme sürümü:
  <https://www.microsoft.com/en-us/evalcenter/download-windows-11-enterprise>
- En az 8 GB boş RAM ve ~90 GB disk.

## Hızlı yol (tam otomatik)

```powershell
# Hyper-V hazır mı?
.\test\New-TestVm.ps1 -CheckOnly

# VM'i kur, Windows'u kur, repoyu kopyala, testi çalıştır:
.\test\New-TestVm.ps1 -IsoPath 'D:\iso\Win11EnterpriseEval.iso' `
                      -GuestUser Test -GuestPass 'Test1234!'
```

Süre: ISO indirme hariç ~20-30 dk (Windows kurulumu) + ~2 dk (test). Sonuç `D:\VMs\WarpCordTest\test-result.json` dosyasına yazılır.

Temizlik: `.\test\New-TestVm.ps1 -Destroy`

## Manuel yol

1. Hyper-V'de Gen2 VM oluştur: 6 GB RAM, 4 vCPU, 80 GB VHDX, **vTPM açık**, **Secure Boot açık**, ISO'yu DVD'ye tak.
2. `test\autounattend.xml` dosyasını ayrı bir ISO'ya koyup ikinci DVD olarak ekle (ya da kurulum sırasında elle ilerle).
3. Kurulum bitince repoyu VM'e kopyala (RDP panosu veya paylaşılan klasör).
4. VM içinde yönetici PowerShell:
   ```powershell
   cd C:\Users\Test\Desktop\WarpCord
   .\test\Test-CleanWindows.ps1
   ```

## Beklenen sonuç

```
  SONUC: PASS (18/18)
  Rapor: C:\ProgramData\WarpCord-test-result.json
```

Kontroller: yönetici, 64-bit, FullLanguage, temiz kurulum, MOTW uygulandı, RemoteSigned, launcher çıkış kodu, `state.json`, sürüm 3.x, sing-box, TUN, görevler, MOTW temizliği, `install.log` hatasız, Discord DNS/API, `tani` çalışması.

> Bu depodaki makinede (Windows 11 25H2, 26200) aynı test **18/18 PASS** verdi: 26 dosyaya MOTW eklendi, RemoteSigned altında tek tık kurulum 43 sn sürdü.

## Sorun giderme

| Belirti | Olası neden |
|---|---|
| `New-TestVm.ps1` "Hyper-V yok" | Rol kurulu değil veya yeniden başlatma yapılmadı |
| VM açılmıyor, "boot failed" | vTPM/Secure Boot kapalı ya da ISO bozuk (3 GB'tan büyük olmalı) |
| VM ağ yok | `Default Switch` yok; `-SwitchName` ile mevcut switch'i verin |
| Testte kurulum düşüyor | VM içinde `tani.bat` çalıştırın; rapor `C:\ProgramData\DiscordTunnel\diagnose-*.log` |
| PowerShell Direct bağlanmıyor | VM'de hesap/parola hazır değil; 2-3 dk bekleyip tekrar deneyin |
