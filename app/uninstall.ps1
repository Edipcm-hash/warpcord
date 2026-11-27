# ============================================================================
#  WarpCord uninstall  -  stops and removes everything the installer created:
#  both scheduled tasks, firewall rules, files, and restores the adapter DNS.
# ============================================================================
param(
    [switch]$KeepDns,
    [switch]$NoElevate,
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Continue'
$RepoDir = if ($RepoRoot) { $RepoRoot.TrimEnd('\') } else { Split-Path -Parent $PSScriptRoot }
if ($RepoDir -and (Test-Path $RepoDir)) {
    try { Get-ChildItem -Path $RepoDir -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue } catch { } } } catch { }
}
$InstallDir = 'C:\ProgramData\DiscordTunnel'
. (Join-Path $RepoDir 'tools\warpcore.ps1')

if (-not (Test-WarpAdmin) -and -not $NoElevate) {
    Write-Host 'Yonetici haklari gerekiyor, UAC istemi aciliyor...' -ForegroundColor Yellow
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    if ($KeepDns) { $argList += '-KeepDns' }
    try { Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null; exit 0 } catch { exit 3 }
}
if (-not (Test-WarpAdmin)) {
    Write-Host 'Kaldirma icin yonetici haklari gerekiyor.' -ForegroundColor Red
    exit 3
}

Initialize-WarpTls
Write-Host ''
Write-Host '  W A R P C O R D   -   kaldirma' -ForegroundColor Cyan
Write-Host ''

Write-WarpSection 'Gorevler'
foreach ($task in @($script:WarpCordFragTask, $script:WarpCordTaskName, $script:WarpCordHealTask)) {
    try { Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue } catch { }
    try { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue } catch { }
    & schtasks /end /tn "$task" 2>&1 | Out-Null
    & schtasks /delete /tn "$task" /f 2>&1 | Out-Null
    Write-WarpLog "$task kaldirildi" 'OK'
}
Get-Process -Name 'sing-box', 'warp-frag' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-WarpSection 'Guven ayarlari'
Remove-WarpHostTrust -Paths @($RepoDir, $InstallDir) -Processes @('sing-box.exe', 'warp-frag.exe')
Write-WarpLog 'Defender/CFA istisnalari geri alindi' 'OK'

Write-WarpSection 'Guvenlik duvari'
& netsh advfirewall firewall delete rule name="$($script:WarpCordFirewall)" 2>&1 | Out-Null
& netsh advfirewall firewall delete rule name="$($script:WarpCordLegacyFirewall)" 2>&1 | Out-Null
Get-NetFirewallRule -DisplayName 'WarpCord *' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Write-WarpLog 'guvenlik duvari kurallari silindi' 'OK'

Write-WarpSection 'DNS'
if ($KeepDns) {
    Write-WarpLog 'DNS degistirilmedi (-KeepDns)' 'INFO'
} elseif (Restore-WarpDns -InstallDir $InstallDir) {
    Write-WarpLog 'adaptor DNS eski haline donduruldu' 'OK'
} else {
    Write-WarpLog 'geri alinacak DNS yedegi yok' 'INFO'
}

Write-WarpSection 'Dosyalar'
if (Test-Path $InstallDir) {
    try {
        Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction Stop
        Write-WarpLog "$InstallDir silindi" 'OK'
    } catch {
        Write-WarpLog "klasor silinemedi: $($_.Exception.Message)" 'WARN'
        Write-WarpLog 'Tunel dosyalari kilitli olabilir; bir dakika sonra kaldir.bat tekrar deneyin.' 'INFO'
    }
}

Write-Host ''
Write-Host '  Kaldirma tamamlandi. Internet baglantiniz normal haline dondu.' -ForegroundColor Green
Write-Host ''
exit 0
