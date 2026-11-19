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

if (Test-Path $InstallDir) {
    try {
        Remove-Item -Recurse -Force $InstallDir -ErrorAction Stop
        Write-Host "Dosyalar silindi: $InstallDir"
    } catch {
        Write-Host "UYARI: $InstallDir silinemedi: $($_.Exception.Message)"
    }
}

Write-Host 'Kaldirma tamamlandi.'
