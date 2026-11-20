#Requires -RunAsAdministrator
# Stops and removes the WarpCord tunnel: task, firewall rule, files.
$ErrorActionPreference = 'Continue'
$InstallDir = 'C:\ProgramData\DiscordTunnel'
$TaskName   = 'DiscordOnlyTunnel'

Write-Host 'Gorev durduruluyor...'
try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch {}
try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue } catch {}
& schtasks /end /tn $TaskName 2>$null | Out-Null
& schtasks /delete /tn $TaskName /f 2>$null | Out-Null

Get-Process sing-box -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host 'Firewall kurali kaldiriliyor...'
& netsh advfirewall firewall delete rule name="DiscordTunnel" 2>&1 | Out-Null

# adaptor DNS'ini eski haline dondur (statik ise ayni adresler, degilse DHCP)
$backupFile = Join-Path $InstallDir 'dns-backup.json'
if (Test-Path $backupFile) {
    try {
        $backup = Get-Content $backupFile -Raw | ConvertFrom-Json
        $alias = $backup.interface
        if ($alias) {
            $restored = $backup.static
            if ($restored) {
                $servers = ($restored -split '[, ]+') | Where-Object { $_ }
                Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses $servers -ErrorAction Stop
                Write-Host "DNS geri alindi (statik): $alias -> $($servers -join ', ')"
            } else {
                Set-DnsClientServerAddress -InterfaceAlias $alias -ResetServerAddresses -ErrorAction Stop
                Write-Host "DNS geri alindi (DHCP): $alias"
            }
        }
    } catch {
        Write-Host "UYARI: DNS geri alinamadi: $($_.Exception.Message)"
    }
}
Clear-DnsClientCache -ErrorAction SilentlyContinue
& ipconfig /flushdns 2>&1 | Out-Null

if (Test-Path $InstallDir) {
    try {
        Remove-Item -Recurse -Force $InstallDir -ErrorAction Stop
        Write-Host "Dosyalar silindi: $InstallDir"
    } catch {
        Write-Host "UYARI: $InstallDir silinemedi: $($_.Exception.Message)"
    }
}

Write-Host 'Kaldirma tamamlandi.'
