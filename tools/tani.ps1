# ============================================================================
#  WarpCord tani (v3)  -  why did the install fail on this machine?
#
#  Read-only diagnostic sweep: Windows build, execution policy, language mode,
#  Smart App Control / WDAC, Defender + ASR + controlled folder access,
#  mark-of-the-web, proxy/TLS/DNS reachability, install state, tasks, logs and
#  recent Defender/ASR events. Prints a verdict with concrete next steps and
#  writes the whole report to C:\ProgramData\DiscordTunnel\diagnose-*.log.
#
#  Usage:  tani.bat  [ -Bundle ] [ -NoElevate ]
# ============================================================================
param(
    [string]$InstallDir = 'C:\ProgramData\DiscordTunnel',
    [switch]$Bundle,
    [switch]$NoElevate
)

$ErrorActionPreference = 'Continue'
$ToolsDir = $PSScriptRoot
$RepoDir = Split-Path -Parent $ToolsDir
try {
    Get-ChildItem -Path $RepoDir -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
        try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue } catch { }
    }
} catch { }
. (Join-Path $ToolsDir 'warpcore.ps1')

if (-not (Test-WarpAdmin) -and -not $NoElevate) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    if ($Bundle) { $argList += '-Bundle' }
    try { Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null; exit 0 } catch { }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:LogPath = Join-Path $env:TEMP "WarpCord-tani-$stamp.log"
$findings = New-Object System.Collections.ArrayList

function Out-Line {
    param([string]$Text = '', [string]$Color = 'Gray')
    Write-Host $Text -ForegroundColor $Color
    try { Add-Content -Path $script:LogPath -Value $Text -Encoding UTF8 } catch { }
}

function Section {
    param([string]$Title)
    Out-Line ''
    Out-Line ("== $Title " + ('=' * [Math]::Max(0, 58 - $Title.Length))) 'Cyan'
}

function Item {
    param([string]$Label, [string]$Value, [string]$Color = 'White')
    Out-Line ("  {0,-24} {1}" -f $Label, $Value) $Color
}

function Add-Finding {
    param([string]$Level, [string]$Text)
    [void]$findings.Add([pscustomobject]@{ Level = $Level; Text = $Text })
}

Write-Host ''
Write-Host '  W A R P C O R D   -   tani' -ForegroundColor Cyan
Write-Host '  ------------------------------------------------------------------' -ForegroundColor DarkGray
Out-Line ("rapor: " + $script:LogPath)

# ------------------------------------------------------------------ system --
Section 'Sistem'
$nt = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue
$os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction SilentlyContinue
$build = [Environment]::OSVersion.Version
$osName = if ([int]$build.Build -ge 22000) { 'Windows 11' } else { [string]$nt.ProductName }
Item 'Windows' ("{0} {1} (build {2}.{3})" -f $osName, [string]$nt.DisplayVersion, $build.Major, $build.Build)
Item 'UBR' ([string]$nt.UBR)
Item 'Bilgisayar' ("{0} ({1})" -f $env:COMPUTERNAME, [string]$os.OSArchitecture)
Item 'PowerShell' ("{0} ({1}-bit)" -f $PSVersionTable.PSVersion, $(if ([Environment]::Is64BitProcess) { '64' } else { '32' }))
Item 'Dil modu' ([string]$ExecutionContext.SessionState.LanguageMode) $(if ([string]$ExecutionContext.SessionState.LanguageMode -eq 'FullLanguage') { 'Green' } else { 'Red' })
Item 'Yonetici' ([string](Test-WarpAdmin)) $(if (Test-WarpAdmin) { 'Green' } else { 'Yellow' })
$uacKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
$uac = Get-ItemProperty -Path $uacKey -ErrorAction SilentlyContinue
Item 'UAC' ("EnableLUA={0} ConsentPrompt={1}" -f $uac.EnableLUA, $uac.ConsentPromptBehaviorAdmin)
$secureBoot = 'bilinmiyor'
try { $secureBoot = [string](Confirm-SecureBootUEFI) } catch { }
Item 'Secure Boot' $secureBoot
$machineModulePath = [string][Environment]::GetEnvironmentVariable('PSModulePath', 'Machine')
$userModulePath = [string][Environment]::GetEnvironmentVariable('PSModulePath', 'User')
if (($machineModulePath + ';' + $userModulePath) -match '(?i)windowsapps|PowerShell\\7') {
    Add-Finding 'WARN' 'PSModulePath degeri PowerShell 7 modullerini gosteriyor; Windows PowerShell 5.1 yanlis modul yukleyip Get-ExecutionPolicy/Defender cmdletlerini kirar. WarpCord bunu kendi surecinde duzeltir.'
}
$modulePathText = ($machineModulePath + ';' + $userModulePath)
Item 'PSModulePath' $(if ($modulePathText.Length -gt 70) { $modulePathText.Substring(0, 70) + '...' } else { $modulePathText })

# ---------------------------------------------------------------- policies --
Section 'Politikalar'
foreach ($scope in 'MachinePolicy', 'UserPolicy', 'LocalMachine', 'CurrentUser', 'Process') {
    Item ("ExecutionPolicy/$scope") ([string](Get-ExecutionPolicy -Scope $scope))
}
$posture = Get-WarpSecurityPosture
if ($posture.PolicyEnforcedByGpo) { Add-Finding 'WARN' 'ExecutionPolicy bir GPO ile zorlaniyor olabilir; imzasiz betikler -File ile calismaz, -Command fallback gerekir.' }
Item 'Smart App Control' ([string]$posture.SmartAppControl) $(if ($posture.SmartAppControl -eq 'on') { 'Red' } else { 'Green' })
if ($posture.SmartAppControl -eq 'on') { Add-Finding 'ERROR' 'Smart App Control acik: imzasiz WarpCord.exe, sing-box ve warp-frag calismaz. Windows Security > App & browser control > Smart App Control kapatilmali (geri donusu yoktur).' }
Item 'WDAC/UMCI zorlama' ([string]$posture.WdacEnforced) $(if ($posture.WdacEnforced) { 'Red' } else { 'Green' })
if ($posture.WdacEnforced) { Add-Finding 'ERROR' 'WDAC/UMCI zorlamasi etkin: imzasiz betik ve ikili engellenir; tani/install calismaz.' }
$motw = Get-WarpMotwCount -Path $RepoDir
Item 'MOTW (internet isareti)' ("{0} dosya" -f $motw) $(if ($motw -gt 0) { 'Yellow' } else { 'Green' })
if ($motw -gt 0) { Add-Finding 'WARN' ("{0} dosyada MOTW var; RemoteSigned politikasinda 'not digitally signed' hatasi verir. WarpCord.bat bunlari acilista temizler." -f $motw) }

# ---------------------------------------------------------------- defender --
Section 'Defender'
if (-not $posture.DefenderPresent) {
    Item 'Durum' 'yok / erisilemiyor (custom Windows olabilir)' 'Yellow'
} else {
    Item 'Gercek zamanli' ([string]$posture.DefenderRealTime) $(if ($posture.DefenderRealTime) { 'Yellow' } else { 'Green' })
    Item 'Tamper Protection' ([string]$posture.DefenderTamper) $(if ($posture.DefenderTamper) { 'Yellow' } else { 'Green' })
    Item 'Controlled Folder Access' ([string]$posture.ControlledFolderAccess) $(if ($posture.ControlledFolderAccess -ne 0) { 'Yellow' } else { 'Green' })
    Item 'ASR etkin kural' ("{0} adet" -f @($posture.AsrEnabledIds).Count) $(if (@($posture.AsrEnabledIds).Count -gt 0) { 'Yellow' } else { 'Green' })
    $asrNames = @{
        '5BEB7EFE-FD9A-4556-801D-275E5FFC04CC' = 'Block executable content from email/webmail'
        'D3E037E1-3EB8-44C8-A917-57927947596D' = 'Block JS/VBS from executing downloaded content'
        'BE9BA2D9-53EA-4CDC-84E5-9B1EEEE46550' = 'Block executable files unless prevalence/age/trusted'
    }
    foreach ($id in @($posture.AsrEnabledIds)) {
        if ($asrNames.ContainsKey([string]$id)) { Item '  ASR' $asrNames[[string]$id] 'Yellow' }
    }
    $trust = @($posture.AsrEnabledIds) -contains '5BEB7EFE-FD9A-4556-801D-275E5FFC04CC'
    if ($trust) { Add-Finding 'WARN' 'ASR "Block executable content from email/webmail" acik: indirilen/zip cikarilan dosyalar engellenebilir; MOTW temizligi sart.' }
    try {
        $excl = @((Get-MpPreference -ErrorAction Stop).ExclusionPath)
        Item 'Istisna (exclusion)' ("{0} yol" -f $excl.Count) 'Green'
        if (-not ($excl -contains $InstallDir)) { Add-Finding 'WARN' 'Kurulum klasoru Defender istisnalarinda yok; kurulumda eklenir, eklenemezse imzasiz sing-box karantinaya alinabilir.' }
    } catch { }
}

# ----------------------------------------------------------------- network --
Section 'Ag'
foreach ($a in @(Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' })) {
    Item 'Adaptor' ("{0} [{1}]" -f $a.Name, $a.InterfaceDescription)
}
$route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric | Select-Object -First 1
if ($route) { Item 'Varsayilan rota' ("{0} gw {1}" -f $route.InterfaceAlias, $route.NextHop) }
$dnsServers = @(Get-DnsClientServerAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.ServerAddresses } | ForEach-Object { $_.ServerAddresses }) -join ', '
Item 'DNS sunuculari' $dnsServers
$proxyEnable = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyEnable -ErrorAction SilentlyContinue).ProxyEnable
$proxyServer = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyServer -ErrorAction SilentlyContinue).ProxyServer
Item 'Proxy (IE/WinINET)' ("enable={0} server={1}" -f $proxyEnable, $proxyServer) $(if ($proxyEnable -eq 1) { 'Yellow' } else { 'Green' })
if ($proxyEnable -eq 1) { Add-Finding 'WARN' 'Sistem proxy acik: indirmeler proxy uzerinden gider, kurumsal proxy TLS denetimi yapiyorsa GitHub baglantisi kirilir.' }
$winhttp = (& netsh winhttp show proxy 2>&1 | Out-String).Trim()
Out-Line ("  WinHTTP: " + ($winhttp -replace "`r?`n", ' '))

$dns = Test-DiscordDns
Item 'discord.com DNS' $dns.Detail $(if ($dns.Ok) { 'Green' } else { 'Red' })
$gh = Invoke-WarpTlsProbe -HostName 'github.com' -Path '/' -TimeoutSec 8 -Expect 'GitHub'
if (-not $gh.Ok) { Start-Sleep -Milliseconds 800; $gh = Invoke-WarpTlsProbe -HostName 'github.com' -Path '/' -TimeoutSec 12 -Expect 'GitHub' }
Item 'github.com TLS' ("{0} ms" -f $gh.Ms) $(if ($gh.Ok) { 'Green' } else { 'Red' })
$needDownload = (-not (Test-Path (Join-Path $InstallDir 'sing-box.exe'))) -or (-not (Test-Path (Join-Path $InstallDir 'wintun.dll')))
if (-not $gh.Ok) {
    if ($needDownload) {
        Add-Finding 'ERROR' 'GitHub TLS erisimi yok ve sing-box/wintun eksik: indirme mumkun degil. Proxy/DNS/guvenlik duvari; cozum: bin\ klasorune dosyalari elle koyun.'
    } else {
        Add-Finding 'WARN' 'GitHub TLS erisimi yok ama sing-box/wintun zaten kurulu; yeniden indirme gerekirse bin\ klasorune dosyalari elle koyun.'
    }
}
$api = Invoke-WarpTlsProbe -HostName 'api.github.com' -Path '/repos/SagerNet/sing-box/releases/latest' -TimeoutSec 10 -Expect 'tag_name'
Item 'api.github.com' ("{0} ms" -f $api.Ms) $(if ($api.Ok) { 'Green' } else { 'Yellow' })

# --------------------------------------------------------------- install --
Section 'Kurulum'
Item 'Klasor' $InstallDir $(if (Test-Path $InstallDir) { 'Green' } else { 'Yellow' })
if (Test-Path $InstallDir) {
    foreach ($f in 'sing-box.exe', 'wintun.dll', 'warp-frag.exe', 'config.json', 'account.json', 'state.json') {
        $p = Join-Path $InstallDir $f
        Item ('  ' + $f) $(if (Test-Path $p) { 'var' } else { 'YOK' }) $(if (Test-Path $p) { 'Green' } else { 'Yellow' })
    }
    $cache = Get-WarpCacheDir $InstallDir
    if (Test-Path $cache) {
        $cached = @(Get-ChildItem $cache -File -ErrorAction SilentlyContinue).Count
        Item '  cache' ("{0} dosya" -f $cached) 'Green'
    }
}
$state = Get-WarpState $InstallDir
if ($state) {
    Item 'State' ("surum {0} | mod {1} | kurulum {2}" -f $state.version, $state.mode, $state.installedAt)
} else {
    Item 'State' 'yok (kurulum hic tamamlanmamis)' 'Yellow'
    Add-Finding 'ERROR' 'state.json yok: kurulum tamamlanmamis. install.log son satirlarina bakin.'
}
foreach ($name in @($script:WarpCordTaskName, $script:WarpCordFragTask, $script:WarpCordHealTask)) {
    $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if ($task) {
        $info = Get-ScheduledTaskInfo -TaskName $name -ErrorAction SilentlyContinue
        Item ('  gorev ' + $name) ("{0} | sonuc {1}" -f $task.State, $info.LastTaskResult) 'Green'
    } else {
        Item ('  gorev ' + $name) 'yok' 'Yellow'
    }
}
foreach ($proc in 'sing-box', 'warp-frag') {
    $p = @(Get-Process -Name $proc -ErrorAction SilentlyContinue)
    Item ('  proses ' + $proc) $(if ($p.Count -gt 0) { "calisiyor (pid $($p[0].Id))" } else { 'yok' }) $(if ($p.Count -gt 0) { 'Green' } else { 'Yellow' })
}
$tun = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
Item '  TUN' $(if ($tun) { "$($tun.Name) [$($tun.Status)]" } else { 'yok' }) $(if ($tun -and $tun.Status -eq 'Up') { 'Green' } else { 'Yellow' })

# ----------------------------------------------------------------- events --
Section 'Son olaylar (Defender / ASR)'
try {
    $events = @(Get-WinEvent -FilterHashtable @{
            LogName   = 'Microsoft-Windows-Windows Defender/Operational'
            Id        = 1116, 1117, 1121, 1122
            StartTime = (Get-Date).AddDays(-7)
        } -MaxEvents 8 -ErrorAction Stop)
    if ($events.Count -eq 0) { Item 'kayit' 'son 7 gunde yok' 'Green' }
    foreach ($ev in $events) {
        $msg = ([string]$ev.Message -split "`n")[0]
        if ($msg.Length -gt 120) { $msg = $msg.Substring(0, 120) + '...' }
        Item ("  " + $ev.Id) $msg 'Yellow'
    }
} catch {
    Item 'kayit' 'okunamadi (yonetici veya log kapali)' 'DarkGray'
}

# ------------------------------------------------------------------ logs --
Section 'Log kuyruklari'
foreach ($logName in 'install.log', 'heal.log') {
    $path = Join-Path $InstallDir $logName
    if (Test-Path $path) {
        $tail = @(Get-Content $path -Tail 8 -ErrorAction SilentlyContinue)
        Out-Line ("  --- $logName ---") 'DarkCyan'
        foreach ($line in $tail) { Out-Line ("  | " + $line) 'DarkGray' }
        if (($tail -join "`n") -match '\[FAIL\]') { Add-Finding 'ERROR' ("{0} icinde hata satiri var; yukaridaki kuyruga bakin." -f $logName) }
    }
}

# ---------------------------------------------------------------- verdict --
Section 'Sonuc'
if ($findings.Count -eq 0) {
    Out-Line '  Belirgin bir engel bulunamadi.' 'Green'
} else {
    foreach ($f in $findings) {
        $color = switch ($f.Level) { 'ERROR' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
        Out-Line ("  [{0}] {1}" -f $f.Level, $f.Text) $color
    }
}
Out-Line ''
Out-Line '  Sirada: (1) WarpCord.bat ile tekrar deneyin  (2) olmazsa onar.bat  (3) hala olmazsa bu raporu paylasin.' 'White'
Out-Line ("  Rapor: " + $script:LogPath) 'DarkGray'

if ($Bundle) {
    try {
        $zip = Join-Path $env:TEMP "WarpCord-tani-$stamp.zip"
        $items = @($script:LogPath)
        foreach ($logName in 'install.log', 'heal.log') {
            $p = Join-Path $InstallDir $logName
            if (Test-Path $p) { $items += $p }
        }
        Compress-Archive -Path $items -DestinationPath $zip -Force -ErrorAction Stop
        Out-Line ("  Paket: " + $zip) 'Green'
    } catch {
        Out-Line ("  Paket olusturulamadi: " + $_.Exception.Message) 'Yellow'
    }
}

if (@($findings | Where-Object { $_.Level -eq 'ERROR' }).Count -gt 0) { exit 1 }
exit 0
