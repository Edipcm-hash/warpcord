# SAC-safe DPI mode (experimental): a Discord-only PAC file pointing at a
# fragmenting proxy hosted by powershell.exe. No sing-box, no TUN, no unsigned
# exe - but also no IP hiding, and voice UDP still goes direct.
#
#   warpac.ps1 -Start    start the proxy, write the PAC, point WinINET at it
#   warpac.ps1 -Stop     stop the proxy, restore the previous WinINET settings
#   warpac.ps1 -Status   print the current state as JSON
param(
    [switch]$Start,
    [switch]$Stop,
    [switch]$Status,
    [int]$Port = 1081
)

$ErrorActionPreference = 'Continue'

$stateDir = Join-Path $env:LOCALAPPDATA 'WarpCord\sac'
$pacPath = Join-Path $stateDir 'proxy.pac'
$pidPath = Join-Path $stateDir 'proxy.pid'
$logPath = Join-Path $stateDir 'proxy.log'
$backupPath = Join-Path $stateDir 'wininet-backup.json'
$hostScript = Join-Path $PSScriptRoot 'warphost.ps1'
$regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'

function Write-State([hashtable]$row) {
    Write-Output ($row | ConvertTo-Json -Compress)
}

function Get-RunningPid {
    if (-not (Test-Path $pidPath)) { return 0 }
    $raw = (Get-Content $pidPath -Raw -ErrorAction SilentlyContinue)
    $value = 0
    if (-not [int]::TryParse([string]$raw, [ref]$value)) { return 0 }
    $process = Get-Process -Id $value -ErrorAction SilentlyContinue
    if (-not $process) { return 0 }
    return $value
}

function New-Pac([int]$ProxyPort) {
    @"
function FindProxyForURL(url, host) {
  var h = host.toLowerCase();
  if (h == "discord.com" || h == "discord.gg" || h == "discordapp.com" || h == "discordapp.net" ||
      dnsDomainIs(h, ".discord.com") || dnsDomainIs(h, ".discord.gg") ||
      dnsDomainIs(h, ".discordapp.com") || dnsDomainIs(h, ".discordapp.net") ||
      dnsDomainIs(h, ".discord.media") || dnsDomainIs(h, ".discord.dev")) {
    return "PROXY 127.0.0.1:$ProxyPort";
  }
  return "DIRECT";
}
"@
}

function Backup-WinInet {
    if (Test-Path $backupPath) { return }
    $current = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
    $backup = [ordered]@{
        autoConfigUrl = if ($current) { [string]$current.AutoConfigURL } else { '' }
        proxyEnable   = if ($current) { [int]$current.ProxyEnable } else { 0 }
        proxyServer   = if ($current) { [string]$current.ProxyServer } else { '' }
        savedAt       = (Get-Date).ToString('s')
    }
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    $backup | ConvertTo-Json -Compress | Set-Content -LiteralPath $backupPath -Encoding utf8
}

function Restore-WinInet {
    if (-not (Test-Path $backupPath)) { return }
    $backup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrEmpty([string]$backup.autoConfigUrl)) {
        Remove-ItemProperty -Path $regPath -Name 'AutoConfigURL' -ErrorAction SilentlyContinue
    } else {
        Set-ItemProperty -Path $regPath -Name 'AutoConfigURL' -Value ([string]$backup.autoConfigUrl)
    }
    Set-ItemProperty -Path $regPath -Name 'ProxyEnable' -Value ([int]$backup.proxyEnable) -Type DWord -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty([string]$backup.proxyServer)) {
        Remove-ItemProperty -Path $regPath -Name 'ProxyServer' -ErrorAction SilentlyContinue
    } else {
        Set-ItemProperty -Path $regPath -Name 'ProxyServer' -Value ([string]$backup.proxyServer)
    }
    Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
}

if ($Start) {
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    $existing = Get-RunningPid
    if ($existing -gt 0) {
        Write-State @{ ok = $true; running = $true; pid = $existing; note = 'zaten calisiyor' }
        exit 0
    }

    Backup-WinInet
    Set-Content -LiteralPath $pacPath -Value (New-Pac $Port) -Encoding ascii

    $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-NoLogo', '-File', $hostScript,
        '-Port', "$Port", '-LogPath', $logPath)
    $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $argsList -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $stateDir 'host.out') `
        -RedirectStandardError (Join-Path $stateDir 'host.err')
    Set-Content -LiteralPath $pidPath -Value "$($process.Id)" -Encoding ascii
    Start-Sleep -Milliseconds 700

    if ($process.HasExited) {
        $tail = ''
        if (Test-Path $logPath) { $tail = (Get-Content $logPath -Tail 3) -join ' | ' }
        Restore-WinInet
        Write-State @{ ok = $false; running = $false; error = 'proxy baslatilamadi'; detail = $tail }
        exit 2
    }

    $pacUrl = 'file:///' + ($pacPath -replace '\\', '/')
    Set-ItemProperty -Path $regPath -Name 'AutoConfigURL' -Value $pacUrl
    Write-State @{ ok = $true; running = $true; pid = $process.Id; pac = $pacUrl; proxy = "127.0.0.1:$Port" }
    exit 0
}

if ($Stop) {
    $running = Get-RunningPid
    if ($running -gt 0) { Stop-Process -Id $running -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
    Restore-WinInet
    Remove-Item -LiteralPath $pacPath -Force -ErrorAction SilentlyContinue

    $auto = (Get-ItemProperty -Path $regPath -Name 'AutoConfigURL' -ErrorAction SilentlyContinue).AutoConfigURL
    Write-State @{ ok = $true; running = $false; autoConfigUrl = [string]$auto }
    exit 0
}

if ($Status) {
    $running = Get-RunningPid
    $auto = (Get-ItemProperty -Path $regPath -Name 'AutoConfigURL' -ErrorAction SilentlyContinue).AutoConfigURL
    Write-State @{ running = ($running -gt 0); pid = $running; autoConfigUrl = [string]$auto; pac = $pacPath }
    exit 0
}

Write-Output 'kullanim: warpac.ps1 -Start | -Stop | -Status [-Port 1081]'
