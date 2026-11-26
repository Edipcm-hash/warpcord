# ============================================================================
#  WarpCord status  -  read-only health check. No admin rights required
#  (except -Restart, which re-starts the scheduled tasks).
# ============================================================================
param(
    [switch]$Restart,
    [switch]$NoElevate
)

$ErrorActionPreference = 'Continue'
$RepoDir = $PSScriptRoot
$InstallDir = 'C:\ProgramData\DiscordTunnel'
. (Join-Path $RepoDir 'tools\warpcore.ps1')

if ($Restart -and -not (Test-WarpAdmin) -and -not $NoElevate) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath), '-Restart')
    try { Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null; exit 0 } catch { }
}

Initialize-WarpTls

function Write-Value([string]$Label, [string]$Value, [string]$Color = 'White') {
    Write-Host ("  {0,-12} " -f $Label) -ForegroundColor DarkGray -NoNewline
    Write-Host $Value -ForegroundColor $Color
}

Write-Host ''
Write-Host '  W A R P C O R D   -   durum' -ForegroundColor Cyan
Write-Host ('  ' + ('-' * 58)) -ForegroundColor DarkGray

$state = Get-WarpState $InstallDir
if (-not $state) {
    Write-Value 'Kurulum' 'yok (WarpCord.bat calistirin)' 'Yellow'
    Write-Host ''
    exit 2
}

$mode = [string]$state.mode
Write-Value 'Kurulum' ("{0}  (surum {1})" -f $state.installedAt, $state.version)
if ($mode -eq 'warp' -and $state.endpoint) {
    Write-Value 'Mod' ("WARP tuneli - {0}:{1}" -f $state.endpoint.Address, $state.endpoint.Port) 'Cyan'
} elseif ($mode -eq 'dpi') {
    Write-Value 'Mod' ("DPI (TLS parcalama) - port {0}" -f $state.fragPort) 'Cyan'
} else {
    Write-Value 'Mod' $mode 'Cyan'
}

if ($Restart) {
    Write-Host ''
    Write-Host '  gorevler yeniden baslatiliyor...' -ForegroundColor Yellow
    foreach ($task in @($script:WarpCordFragTask, $script:WarpCordTaskName)) {
        $info = Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        if ($info) {
            try { Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue } catch { }
            Start-Sleep -Milliseconds 700
            try { Start-ScheduledTask -TaskName $task; Write-Host "  $task baslatildi" -ForegroundColor Green } catch { Write-Host "  $task baslatilamadi: $($_.Exception.Message)" -ForegroundColor Red }
        }
    }
    Start-Sleep -Seconds 3
}

foreach ($task in @($script:WarpCordTaskName, $script:WarpCordFragTask)) {
    $info = Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
    if ($info) {
        $taskState = (Get-ScheduledTaskInfo -TaskName $task -ErrorAction SilentlyContinue).State
        if (-not $taskState) { $taskState = $info.State }
        $color = if ("$taskState" -match 'Running|Ready') { 'Green' } else { 'Red' }
        Write-Value 'Gorev' ("{0} -> {1}" -f $task, $taskState) $color
    }
}

$singBox = Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue
Write-Value 'sing-box' $(if ($singBox) { "calisiyor (pid $($singBox[0].Id))" } else { 'CALISMIYOR' }) $(if ($singBox) { 'Green' } else { 'Red' })
if ($mode -eq 'dpi') {
    $frag = Get-Process -Name 'warp-frag' -ErrorAction SilentlyContinue
    Write-Value 'proxy' $(if ($frag) { "calisiyor (pid $($frag[0].Id))" } else { 'CALISMIYOR' }) $(if ($frag) { 'Green' } else { 'Red' })
}

$adapter = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
Write-Value 'TUN' $(if ($adapter) { "$($adapter.Name) [$($adapter.Status)]" } else { 'yok' }) $(if ($adapter -and $adapter.Status -eq 'Up') { 'Green' } else { 'Yellow' })

$dns = Test-DiscordDns
Write-Value 'DNS' $dns.Detail $(if ($dns.Ok) { 'Green' } else { 'Red' })

$api = Test-DiscordApi
$apiText = if ($api.Ok) { "OK ($($api.Ms) ms)" } else { "HATA - $($api.Detail)" }
if ($apiText.Length -gt 90) { $apiText = $apiText.Substring(0, 90) + '...' }
Write-Value 'Discord API' $apiText $(if ($api.Ok) { 'Green' } else { 'Red' })

$otherOk = $false
try {
    $other = Invoke-WebRequest -Uri 'https://example.com/' -UseBasicParsing -TimeoutSec 12
    $otherOk = $other.StatusCode -eq 200
} catch { }
Write-Value 'Diger trafik' $(if ($otherOk) { 'OK (direkt)' } else { 'BOZUK' }) $(if ($otherOk) { 'Green' } else { 'Red' })

$publicIp = Get-PublicIp
if ($publicIp) { Write-Value 'Dis IP' $publicIp 'Gray' }

$tunnelLog = Join-Path $InstallDir 'tunnel.log'
if (Test-Path $tunnelLog) {
    $hits = @(Get-Content $tunnelLog -Tail 60 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'wireguard\[warp\]|http\[frag\]' }).Count
    $direct = @(Get-Content $tunnelLog -Tail 60 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'outbound/direct' }).Count
    Write-Value 'Log' "$tunnelLog (tunel $hits / direkt $direct)" 'DarkGray'
}

$healthy = ($singBox -ne $null) -and $dns.Ok -and $api.Ok
if ($mode -eq 'dpi') { $healthy = $healthy -and ($null -ne (Get-Process -Name 'warp-frag' -ErrorAction SilentlyContinue)) }

Write-Host ('  ' + ('-' * 58)) -ForegroundColor DarkGray
if ($healthy) {
    Write-Host '  SONUC: SAGLIKLI - Discord tuneli calisiyor.' -ForegroundColor Green
    Write-Host ''
    exit 0
} else {
    Write-Host '  SONUC: SORUN VAR.' -ForegroundColor Red
    Write-Host '  Cozum: WarpCord.bat dosyasini yeniden calistirin (onarir).' -ForegroundColor Yellow
    Write-Host '  Log : C:\ProgramData\DiscordTunnel\install.log' -ForegroundColor DarkGray
    Write-Host ''
    if (Test-Path $tunnelLog) {
        Write-Host '  son log satirlari:' -ForegroundColor DarkGray
        Get-Content $tunnelLog -Tail 12 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
    Write-Host ''
    exit 2
}
