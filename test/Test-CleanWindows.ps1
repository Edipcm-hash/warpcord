# ============================================================================
#  WarpCord clean-room test  (run INSIDE the fresh Windows 11 VM)
#
#  Simulates what a user gets from a downloaded zip on a stock Windows 11:
#    * mark-of-the-web on every file  (RemoteSigned + SmartScreen path)
#    * CurrentUser execution policy = RemoteSigned
#    * a completely empty install (optional full uninstall first)
#  then runs the single-click installer and asserts the result.
#
#  Usage (elevated PowerShell inside the VM):
#    .\Test-CleanWindows.ps1                 # from scratch, MOTW, RemoteSigned
#    .\Test-CleanWindows.ps1 -KeepTunnel     # keep an existing install
#    .\Test-CleanWindows.ps1 -Mode dpi       # force the DPI engine
#
#  Writes C:\ProgramData\WarpCord-test-result.json and exits 0 only if every
#  check passes.
# ============================================================================
param(
    [string]$RepoDir = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('auto', 'warp', 'dpi')][string]$Mode = 'auto',
    [switch]$KeepTunnel,
    [switch]$SkipMotw,
    [string]$ResultPath = 'C:\ProgramData\WarpCord-test-result.json'
)

$ErrorActionPreference = 'Continue'
$InstallDir = 'C:\ProgramData\DiscordTunnel'

function Write-Step { param([string]$Text) Write-Host ""; Write-Host "### $Text" -ForegroundColor Cyan }
function Write-Ok { param([string]$Text) Write-Host "  [ OK ] $Text" -ForegroundColor Green }
function Write-Bad { param([string]$Text) Write-Host "  [FAIL] $Text" -ForegroundColor Red }

$results = New-Object System.Collections.ArrayList
function Add-Check {
    param([string]$Name, [bool]$Pass, [string]$Detail = '')
    [void]$results.Add([pscustomobject]@{ name = $Name; pass = $Pass; detail = $Detail })
    if ($Pass) { Write-Ok "$Name $Detail" } else { Write-Bad "$Name $Detail" }
}

if (-not (Test-Path $RepoDir)) { throw "Repo bulunamadi: $RepoDir" }
. (Join-Path $RepoDir 'tools\warpcore.ps1')

# ------------------------------------------------------------- elevation ----
if (-not (Test-WarpAdmin)) {
    $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath), '-RepoDir', ('"{0}"' -f $RepoDir), '-Mode', $Mode)
    if ($KeepTunnel) { $args2 += '-KeepTunnel' }
    if ($SkipMotw) { $args2 += '-SkipMotw' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args2 | Out-Null
    exit 0
}

$testStart = Get-Date
Write-Host ''
Write-Host '  W A R P C O R D   -   clean Windows test' -ForegroundColor Cyan
Write-Host ('  ' + ('-' * 60)) -ForegroundColor DarkGray

# --------------------------------------------------------------- baseline ---
Write-Step 'Ortam'
$posture = Get-WarpSecurityPosture
$build = [Environment]::OSVersion.Version
Write-Host ("  Windows build {0}.{1} | {2}-bit | PowerShell {3}" -f $build.Major, $build.Build, $(if ([Environment]::Is64BitOperatingSystem) { '64' } else { '32' }), $PSVersionTable.PSVersion)
Write-Host ("  politika={0} | Defender={1} | SAC={2} | WDAC={3} | CFA={4}" -f $posture.ExecutionPolicy, $posture.DefenderPresent, $posture.SmartAppControl, $posture.WdacEnforced, $posture.ControlledFolderAccess)
Add-Check 'Yonetici yetkisi' (Test-WarpAdmin)
Add-Check '64-bit Windows' ([Environment]::Is64BitOperatingSystem)
Add-Check 'FullLanguage' ($posture.LanguageMode -eq 'FullLanguage') $posture.LanguageMode

if (-not $KeepTunnel) {
    Write-Step 'Onceki kurulum kaldiriliyor'
    $uninstall = Join-Path $RepoDir 'app\uninstall.ps1'
    if (Test-Path $uninstall) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstall -KeepDns 2>&1 | Out-Null
    }
    Start-Sleep -Seconds 1
    Add-Check 'Kurulum klasoru silindi' (-not (Test-Path $InstallDir))
}

# --------------------------------------------------- stock-Windows stimuli --
Write-Step 'Stock Windows 11 kosullari'
if (-not $SkipMotw) {
    $files = @(Get-ChildItem $RepoDir -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\\.git\\' })
    foreach ($file in $files) {
        try { "`r`n[ZoneTransfer]`r`nZoneId=3`r`n" | Set-Content -LiteralPath $file.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue } catch { }
    }
    $motw = Get-WarpMotwCount -Path $RepoDir
    Add-Check 'MOTW uygulandi' ($motw -gt 0) ("$motw dosya")
}
try { Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction Stop } catch { }
Add-Check 'RemoteSigned politika' ((Get-ExecutionPolicy -Scope CurrentUser) -eq 'RemoteSigned') ([string](Get-ExecutionPolicy -Scope CurrentUser))

# ------------------------------------------------------------- install ------
Write-Step "Tek tik kurulum (Mode=$Mode)"
$sw = [Diagnostics.Stopwatch]::StartNew()
$bat = Join-Path $RepoDir 'WarpCord.bat'
$output = @()
$outFile = Join-Path $env:TEMP 'warpcord-test.out'
$errFile = Join-Path $env:TEMP 'warpcord-test.err'
$wrapper = Join-Path $env:TEMP 'warpcord-test-run.cmd'
try {
    # The launcher ends with "pause"; the wrapper feeds it a key so the hidden
    # console cannot hang the test run.
    Set-Content -Path $wrapper -Encoding ASCII -Value @(
        '@echo off',
        ('echo.| "{0}" -Mode {1}' -f $bat, $Mode),
        'exit /b %errorlevel%'
    )
    $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $wrapper) -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $exitCode = $proc.ExitCode
    $output = @(Get-Content $outFile -ErrorAction SilentlyContinue)
} catch {
    $exitCode = -1
}
$sw.Stop()
Add-Check 'Launcher cikis kodu' ($exitCode -eq 0) ("exit=$exitCode, $([math]::Round($sw.Elapsed.TotalSeconds,1)) sn")

# ------------------------------------------------------------ assertions ----
Write-Step 'Dogrulama'
Start-Sleep -Seconds 2
Add-Check 'state.json var' (Test-Path (Join-Path $InstallDir 'state.json'))
$state = Get-WarpState $InstallDir
Add-Check 'Surum 3.x' ($state -and ([string]$state.version -like '3.*')) ([string]$state.version)
Add-Check 'sing-box calisiyor' ([bool](Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue))
$tun = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
Add-Check 'TUN adaptoru Up' ([bool]($tun -and $tun.Status -eq 'Up')) ([string]$tun.Status)
Add-Check 'Tunel gorevi kayitli' ([bool](Get-ScheduledTask -TaskName $script:WarpCordTaskName -ErrorAction SilentlyContinue))
Add-Check 'Koruyucu gorev kayitli' ([bool](Get-ScheduledTask -TaskName $script:WarpCordHealTask -ErrorAction SilentlyContinue))
Add-Check 'MOTW temizlendi' ((Get-WarpMotwCount -Path $RepoDir) -eq 0)
$installLog = Join-Path $InstallDir 'install.log'
$fails = @()
if (Test-Path $installLog) { $fails = @(Select-String -Path $installLog -Pattern '\[FAIL\]' -ErrorAction SilentlyContinue) }
Add-Check 'install.log hatasiz' ($fails.Count -eq 0) ("$($fails.Count) FAIL")
$dns = Test-DiscordDns
Add-Check 'Discord DNS' ([bool]$dns.Ok) $dns.Detail
$api = Test-DiscordApi -TimeoutSec 12
Add-Check 'Discord API' ([bool]$api.Ok) ("$($api.Ms) ms")

# ------------------------------------------------------------- tani/status --
Write-Step 'tani.bat'
$tani = Join-Path $RepoDir 'tools\tani.ps1'
$taniOut = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $tani -NoElevate 2>&1
$taniText = ($taniOut | Out-String)
Add-Check 'tani calisti' ($LASTEXITCODE -eq 0 -or $taniText -match 'Belirgin bir engel bulunamadi') ("exit=$LASTEXITCODE")

# ---------------------------------------------------------------- result ----
$passed = @($results | Where-Object { $_.pass }).Count
$total = $results.Count
$verdict = if ($passed -eq $total) { 'PASS' } else { 'FAIL' }
Write-Host ''
Write-Host ("  SONUC: {0} ({1}/{2})  {3} sn" -f $verdict, $passed, $total, [math]::Round(((Get-Date) - $testStart).TotalSeconds, 0)) -ForegroundColor $(if ($verdict -eq 'PASS') { 'Green' } else { 'Red' })

$report = [ordered]@{
    verdict    = $verdict
    passed     = $passed
    total      = $total
    startedAt  = $testStart.ToString('yyyy-MM-dd HH:mm:ss')
    finishedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    os         = ("{0}.{1}" -f $build.Major, $build.Build)
    mode       = $Mode
    checks     = @($results)
    installLog = $installLog
    state      = $state
}
try {
    $dir = Split-Path -Parent $ResultPath
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Write-WarpJsonFile $ResultPath $report
    Write-Host ("  Rapor: {0}" -f $ResultPath) -ForegroundColor DarkGray
} catch { }

if ($verdict -eq 'PASS') { exit 0 }
exit 1
