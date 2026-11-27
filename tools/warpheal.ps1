# ============================================================================
#  WarpCord guardian (v2.5)  -  watches the tunnel and repairs it on its own.
#
#  Runs every 5 minutes from the "WarpCordHeal" scheduled task as SYSTEM, and
#  can be run by hand through onar.bat. Everything it checks, finds or fixes is
#  appended to C:\ProgramData\DiscordTunnel\heal.log, and the last run is
#  summarised in heal-state.json so durum.bat can display it.
#
#  Exit codes:  0 = healthy, nothing to do
#               1 = a problem was found and repaired
#               2 = a problem remains (a full reinstall is needed)
#               3 = administrator rights are missing
# ============================================================================
param(
    [string]$InstallDir = 'C:\ProgramData\DiscordTunnel',
    [switch]$Quiet,
    [switch]$NoElevate,
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'warpcore.ps1')

$healLogPath   = Join-Path $InstallDir 'heal.log'
$healStatePath = Join-Path $InstallDir 'heal-state.json'
$singBoxPath   = Join-Path $InstallDir 'sing-box.exe'
$configPath    = Join-Path $InstallDir 'config.json'
$fragPath      = Join-Path $InstallDir 'warp-frag.exe'
$wintunPath    = Join-Path $InstallDir 'wintun.dll'
$tunnelLogPath = Join-Path $InstallDir 'tunnel.log'

# ------------------------------------------------------- install present? --
if (-not (Test-Path $InstallDir)) {
    Write-Host 'WarpCord kurulu degil (C:\ProgramData\DiscordTunnel yok). WarpCord.bat calistirin.' -ForegroundColor Yellow
    exit 2
}

# ------------------------------------------------------------- elevation --
if (-not (Test-WarpAdmin)) {
    if ($NoElevate) {
        Write-Host 'Onarim icin yonetici haklari gerekiyor.' -ForegroundColor Red
        exit 3
    }
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null
        exit 0
    } catch {
        Write-Host 'UAC reddedildi. onar.bat dosyasina sag tik -> Yonetici olarak calistir.' -ForegroundColor Red
        exit 3
    }
}

# ------------------------------------------------------------------ log ----
function Initialize-HealLog {
    # Append, never truncate: the guardian runs every 5 minutes and the history
    # is the whole point. Rotate at 2 MB, keep one old file.
    if (Test-Path $healLogPath) {
        $size = (Get-Item $healLogPath -ErrorAction SilentlyContinue).Length
        if ($size -gt 2MB) {
            $old = "$healLogPath.old"
            Remove-Item $old -Force -ErrorAction SilentlyContinue
            Move-Item $healLogPath $old -Force -ErrorAction SilentlyContinue
        }
    }
    $script:WarpLogPath = $healLogPath
    try {
        Add-Content -Path $healLogPath -Encoding UTF8 -Value ("`r`n=== guardian {0} (v{1}) ===" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $script:WarpCordVersion)
    } catch { }
}

function Add-Problem {
    param([string]$Text)
    [void]$script:Problems.Add($Text)
    Write-WarpLog $Text 'WARN'
}

function Add-Fix {
    param([string]$Text)
    [void]$script:Fixes.Add($Text)
    Write-WarpLog $Text 'STEP'
}

function Write-TunnelEvidence {
    if (-not (Test-Path $tunnelLogPath)) { return }
    $tail = @(Get-Content $tunnelLogPath -Tail 5 -ErrorAction SilentlyContinue)
    if ($tail.Count -eq 0) { return }
    try {
        Add-Content -Path $healLogPath -Encoding UTF8 -Value '  son tunel satirlari:'
        foreach ($line in $tail) {
            $short = [string]$line
            if ($short.Length -gt 300) { $short = $short.Substring(0, 300) + '...' }
            Add-Content -Path $healLogPath -Encoding UTF8 -Value ("  | " + $short)
        }
    } catch { }
}

# --------------------------------------------------------------- helpers ---
function Test-TunnelProcess {
    return [bool](Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue)
}

function Test-FragProcess {
    return [bool](Get-Process -Name 'warp-frag' -ErrorAction SilentlyContinue)
}

function Test-TunUp {
    $adapter = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
    return [bool]($adapter -and $adapter.Status -eq 'Up')
}

function Wait-TunnelProcess {
    param([int]$Seconds = 20)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        if (Test-TunnelProcess) { return $true }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

function Wait-TunUp {
    param([int]$Seconds = 20)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        if (Test-TunUp) { return $true }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

function Wait-FragPort {
    param([int]$Port, [int]$Seconds = 15)
    if ($Port -le 0) { return $false }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
        if (Test-TcpPort -Port $Port -TimeoutMs 300) { return $true }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

function Test-SingBoxConfig {
    if (-not (Test-Path $singBoxPath)) { return $false }
    if (-not (Test-Path $configPath)) { return $false }
    try {
        $output = & $singBoxPath check -c $configPath 2>&1
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    }
}

# ------------------------------------------------------------------ tasks ---
function New-WarpTask {
    param([string]$Name, [string]$Executable, [string]$Arguments, [string]$Description)
    $action = New-ScheduledTaskAction -Execute $Executable -Argument $Arguments -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Description $Description -Force | Out-Null
}

function Register-TunnelTask {
    try {
        New-WarpTask -Name $script:WarpCordTaskName -Executable $singBoxPath `
            -Arguments ("run -c `"{0}`"" -f $configPath) -Description 'WarpCord - Discord-only tunnel'
        return $true
    } catch {
        Write-WarpLog ("tunel gorevi olusturulamadi: {0}" -f $_.Exception.Message) 'ERROR'
        return $false
    }
}

function Register-FragTask {
    param([int]$Port)
    try {
        New-WarpTask -Name $script:WarpCordFragTask -Executable $fragPath `
            -Arguments ("--port {0} --mode tlsrec --delay 3 --log `"{1}`"" -f $Port, (Join-Path $InstallDir 'frag.log')) `
            -Description 'WarpCord - TLS fragmentation proxy'
        return $true
    } catch {
        Write-WarpLog ("parcalama gorevi olusturulamadi: {0}" -f $_.Exception.Message) 'ERROR'
        return $false
    }
}

function Start-Tunnel {
    try {
        Stop-ScheduledTask -TaskName $script:WarpCordTaskName -ErrorAction SilentlyContinue
    } catch { }
    Start-Sleep -Milliseconds 700
    try {
        Start-ScheduledTask -TaskName $script:WarpCordTaskName -ErrorAction Stop
    } catch {
        # Task layer broken: fall back to a direct process so Discord works now.
        Start-Process -FilePath $singBoxPath -ArgumentList @('run', '-c', $configPath) -WindowStyle Hidden -ErrorAction SilentlyContinue
    }
}

function Start-FragProxy {
    try {
        Stop-ScheduledTask -TaskName $script:WarpCordFragTask -ErrorAction SilentlyContinue
    } catch { }
    Start-Sleep -Milliseconds 500
    try {
        Start-ScheduledTask -TaskName $script:WarpCordFragTask -ErrorAction Stop
    } catch {
        Start-Process -FilePath $fragPath -ArgumentList @('--port', "$($script:CurrentState.fragPort)", '--mode', 'tlsrec', '--delay', '3', '--log', (Join-Path $InstallDir 'frag.log')) -WindowStyle Hidden -ErrorAction SilentlyContinue
    }
}

# ----------------------------------------------------------------- repair ---
function Repair-ConfigFile {
    $state = $script:CurrentState
    try {
        if ([string]$state.mode -eq 'dpi') {
            $config = New-WarpDpiConfig -LogPath $tunnelLogPath -FragPort ([int]$state.fragPort)
        } else {
            $account = Read-WarpJsonFile (Join-Path $InstallDir 'account.json')
            if (-not $account) {
                Write-WarpLog 'account.json yok: config yeniden uretilemiyor' 'ERROR'
                return $false
            }
            $endpoint = $state.endpoint
            if (-not $endpoint) {
                $endpoint = [pscustomobject]@{ Address = $account.EndpointV4; Port = 2408 }
            }
            $config = New-WarpTunnelConfig -Account $account -Endpoint $endpoint -LogPath $tunnelLogPath
        }
        Write-WarpJsonFile $configPath $config
        Set-WarpPrivateAcl $configPath
        return (Test-SingBoxConfig)
    } catch {
        Write-WarpLog ("config onarimi basarisiz: {0}" -f $_.Exception.Message) 'ERROR'
        return $false
    }
}

function Repair-SingBoxBinary {
    $version = [string]$script:CurrentState.singBoxVersion
    if (-not $version) { $version = '1.14.4' }
    $arch = Get-WarpArchitecture
    $temp = Join-Path $env:TEMP ("warpcord-heal-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    try {
        Initialize-WarpTls
        $zip = Join-Path $temp 'sing-box.zip'
        $url = "https://github.com/SagerNet/sing-box/releases/download/v$version/sing-box-$version-windows-$arch.zip"
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 180 -ErrorAction Stop
                break
            } catch {
                Write-WarpLog ("sing-box indirme denemesi {0}/3 basarisiz" -f $attempt) 'WARN'
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
        if (-not (Test-Path $zip)) { return $false }
        $extract = Join-Path $temp 'x'
        try {
            Expand-Archive -Path $zip -DestinationPath $extract -Force -ErrorAction Stop
        } catch {
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
        }
        $exe = Get-ChildItem -Path $extract -Recurse -Filter 'sing-box.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $exe) { return $false }
        Copy-Item $exe.FullName $singBoxPath -Force -ErrorAction Stop
        $check = & $singBoxPath version 2>&1
        return (($check -join "`n") -match 'sing-box version')
    } catch {
        Write-WarpLog ("sing-box indirilemedi: {0}" -f $_.Exception.Message) 'ERROR'
        return $false
    } finally {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Repair-WintunBinary {
    $arch = Get-WarpArchitecture
    $version = [string]$script:CurrentState.wintunVersion
    if (-not $version) { $version = '0.14.1' }
    $temp = Join-Path $env:TEMP ("warpcord-heal-" + [guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    try {
        Initialize-WarpTls
        $zip = Join-Path $temp 'wintun.zip'
        $url = "https://www.wintun.net/builds/wintun-$version.zip"
        try {
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 120 -ErrorAction Stop
        } catch {
            Write-WarpLog ("wintun indirilemedi: {0}" -f $_.Exception.Message) 'ERROR'
            return $false
        }
        $extract = Join-Path $temp 'x'
        try {
            Expand-Archive -Path $zip -DestinationPath $extract -Force -ErrorAction Stop
        } catch {
            return $false
        }
        $dll = Join-Path $extract "wintun\bin\$arch\wintun.dll"
        if (-not (Test-Path $dll)) {
            $dll = (Get-ChildItem -Path $extract -Recurse -Filter 'wintun.dll' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match "\\$arch\\" } | Select-Object -First 1).FullName
        }
        if (-not $dll -or -not (Test-Path $dll)) { return $false }
        Copy-Item $dll $wintunPath -Force -ErrorAction Stop
        return $true
    } catch {
        return $false
    } finally {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ------------------------------------------------------------------- dns ----
function Repair-Dns {
    try {
        $backup = $null
        foreach ($entry in (Get-WarpDnsBackupEntries -InstallDir $InstallDir)) { $backup = $entry; break }
        if (-not $backup) { $backup = Backup-WarpDns -InstallDir $InstallDir }
        if (-not $backup) { return $false }
        return (Set-WarpDns -Backup $backup)
    } catch {
        Write-WarpLog ("DNS onarimi basarisiz: {0}" -f $_.Exception.Message) 'ERROR'
        return $false
    }
}

function Save-HealState {
    param([string]$Status, $State)
    $data = [ordered]@{
        lastRun  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        status   = $Status
        version  = $script:WarpCordVersion
        mode     = [string]$State.mode
        pid      = @(Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue)[0]
        tun      = (Test-TunUp)
        problems = @($script:Problems)
        repairs  = @($script:Fixes)
    }
    if ($data.pid) { $data.pid = $data.pid.Id } else { $data.pid = $null }
    try { Write-WarpJsonFile $healStatePath $data } catch { }
}

# =========================================================================== #
Initialize-HealLog
Initialize-WarpTls
Write-WarpSection 'Koruyucu kontrol'

$state = Get-WarpState $InstallDir
if (-not $state) {
    Write-WarpLog 'state.json yok: kurulum kaydi bulunamadi' 'ERROR'
    exit 2
}
$script:CurrentState = $state
$mode = [string]$state.mode
$script:Problems = New-Object System.Collections.ArrayList
$script:Fixes = New-Object System.Collections.ArrayList
$restarts = 0

Write-WarpLog ("mod={0}, kurulum={1}, surum={2}" -f $mode, $state.installedAt, $state.version) 'INFO'

# ------------------------------------------------------------- 1. binaries --
if (-not (Test-Path $singBoxPath)) {
    Add-Problem 'sing-box.exe eksik'
    if (Repair-SingBoxBinary) { Add-Fix 'sing-box.exe yeniden indirildi' }
}
if (-not (Test-Path $wintunPath)) {
    Add-Problem 'wintun.dll eksik'
    if (Repair-WintunBinary) { Add-Fix 'wintun.dll yeniden indirildi' }
}
if (-not (Test-Path $singBoxPath)) {
    Write-WarpLog 'sing-box olmadan onarim yapilamaz; WarpCord.bat gerekli' 'ERROR'
    Save-HealState 'broken' $state
    exit 2
}
if ($mode -eq 'dpi' -and -not (Test-Path $fragPath)) {
    Add-Problem 'warp-frag.exe eksik (DPI modu calisamaz)'
}

# ---------------------------------------------------------------- 2. tasks --
$tunnelTask = Get-ScheduledTask -TaskName $script:WarpCordTaskName -ErrorAction SilentlyContinue
if (-not $tunnelTask) {
    Add-Problem ("gorev yok: {0}" -f $script:WarpCordTaskName)
    if (Register-TunnelTask) { Add-Fix 'tunel gorevi yeniden olusturuldu' }
} elseif ([string]$tunnelTask.State -eq 'Disabled') {
    Add-Problem 'tunel gorevi devre disi'
    try { Enable-ScheduledTask -TaskName $script:WarpCordTaskName -ErrorAction Stop | Out-Null; Add-Fix 'tunel gorevi etkinlestirildi' } catch { }
}

if ($mode -eq 'dpi') {
    $fragTask = Get-ScheduledTask -TaskName $script:WarpCordFragTask -ErrorAction SilentlyContinue
    if (-not $fragTask) {
        Add-Problem 'parcalama gorevi yok'
        if (Register-FragTask -Port ([int]$state.fragPort)) { Add-Fix 'parcalama gorevi yeniden olusturuldu' }
    }
}

# --------------------------------------------------------------- 3. config --
if (-not (Test-Path $configPath)) {
    Add-Problem 'config.json yok'
    if (Repair-ConfigFile) { Add-Fix 'config.json yeniden uretildi' }
} elseif (-not (Test-SingBoxConfig)) {
    Add-Problem 'config.json gecersiz (sing-box check basarisiz)'
    if (Repair-ConfigFile) { Add-Fix 'config.json onarildi' }
}

# ------------------------------------------------------ 4. process and TUN --
if (-not (Test-TunnelProcess)) {
    Add-Problem 'sing-box calismiyor'
    Write-TunnelEvidence
    if ($restarts -lt 2) {
        $restarts++
        Start-Tunnel
        if (Wait-TunnelProcess -Seconds $WaitSeconds) { Add-Fix 'tunel yeniden baslatildi' }
    }
}

if ((Test-TunnelProcess) -and -not (Wait-TunUp -Seconds 8)) {
    Add-Problem 'TUN adaptoru yok'
    if ($restarts -lt 2) {
        $restarts++
        Start-Tunnel
        if (Wait-TunUp -Seconds $WaitSeconds) { Add-Fix 'TUN adaptoru geri geldi' }
    }
}

# ------------------------------------------------------------ 5. DPI proxy --
if ($mode -eq 'dpi' -and (Test-Path $fragPath)) {
    $port = [int]$state.fragPort
    if (-not (Test-FragProcess)) {
        Add-Problem 'parcalama proxy calismiyor'
        Start-FragProxy
        if (Wait-FragPort -Port $port -Seconds 15) { Add-Fix 'parcalama proxy yeniden baslatildi' }
    } elseif ($port -gt 0 -and -not (Test-TcpPort -Port $port -TimeoutMs 600)) {
        Add-Problem ("proxy portu {0} dinlemiyor" -f $port)
        Start-FragProxy
        if (Wait-FragPort -Port $port -Seconds 15) { Add-Fix 'proxy portu geri geldi' }
    }
}

# ------------------------------------------------------------------- 6. DNS --
$dns = Test-DiscordDns
if (-not $dns.Ok) {
    Add-Problem ("DNS sorunu: {0}" -f $dns.Detail)
    if ($state.changedDns) {
        if (Repair-Dns) { Add-Fix 'DNS 1.1.1.1 olarak yeniden ayarlandi' }
    }
}

# ------------------------------------------------------------- 7. Discord ----
if (Test-TunnelProcess) {
    $api = Test-DiscordApi -TimeoutSec 10
    if (-not $api.Ok) {
        Add-Problem ("Discord API ulasilamiyor: {0}" -f $api.Detail)
        Write-TunnelEvidence
        if ($restarts -lt 2) {
            $restarts++
            Start-Tunnel
            if (Wait-TunnelProcess -Seconds $WaitSeconds) { $api = Test-DiscordApi -TimeoutSec 10 }
            if ($api.Ok) { Add-Fix 'Discord API yeniden erisilebilir' }
        }
    }
}

# ---------------------------------------------------------------- sonuc -----
$healthy = (Test-TunnelProcess) -and (Test-TunUp)
if ($mode -eq 'dpi') { $healthy = $healthy -and (Test-FragProcess) }

if ($healthy -and $script:Fixes.Count -eq 0) {
    Write-WarpLog ("saglikli (pid {0})" -f (@(Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue)[0].Id)) 'OK'
    Save-HealState 'healthy' $state
    exit 0
}
if ($healthy) {
    Write-WarpLog ("onarildi: {0} islem" -f $script:Fixes.Count) 'OK'
    Save-HealState 'repaired' $state
    # A scheduled run reports success: Task Scheduler must not paint a repaired
    # tunnel red. Manual runs keep the richer exit code for scripts.
    if ($Quiet) { exit 0 }
    exit 1
}
Write-WarpLog ("onarilamadi: {0} sorun kaldi" -f $script:Problems.Count) 'ERROR'
Save-HealState 'broken' $state
exit 2
