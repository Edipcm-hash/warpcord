# ============================================================================
#  WarpCord installer  -  one click, no Python, no manual steps.
#
#  Discord-only split tunnel, two engines:
#    * warp : Cloudflare WARP (WireGuard), when the line allows it
#    * dpi  : TLS-record fragmentation, for ISPs that block WARP itself
#             (Superonline) while still filtering Discord by SNI
#
#  Run WarpCord.bat (or this file). It elevates itself through UAC.
# ============================================================================
param(
    [ValidateSet('auto', 'warp', 'dpi')][string]$Mode = 'auto',
    [switch]$Force,
    [string]$Endpoint = '',
    [switch]$SkipProbe,
    [switch]$KeepDns,
    [string]$SingBoxVersion = '1.14.4',
    [string]$WintunVersion = '0.14.1',
    [switch]$NoElevate,
    [switch]$NoSecurityChanges,
    [string]$RepoRoot = '',
    [string]$ExtraApps = $null
)

$ErrorActionPreference = 'Continue'
$RepoDir = if ($RepoRoot) { $RepoRoot.TrimEnd('\') } else { Split-Path -Parent $PSScriptRoot }
# v3: the engine file may carry mark-of-the-web; clear it before dot-sourcing.
if ($RepoDir -and (Test-Path $RepoDir)) {
    try {
        Get-ChildItem -Path $RepoDir -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
            try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue } catch { }
        }
    } catch { }
}
$InstallDir = 'C:\ProgramData\DiscordTunnel'
. (Join-Path $RepoDir 'tools\warpcore.ps1')

# --------------------------------------------------------------- elevation --
if (-not (Test-WarpAdmin) -and -not $NoElevate) {
    Write-Host 'Yonetici haklari gerekiyor, UAC istemi aciliyor...' -ForegroundColor Yellow
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f $PSCommandPath))
    foreach ($key in $PSBoundParameters.Keys) {
        $value = $PSBoundParameters[$key]
        if ($value -is [switch]) {
            if ($value.IsPresent) { $argList += "-$key" }
        } elseif ($null -ne $value -and "$value" -ne '') {
            $argList += @("-$key", ('"{0}"' -f $value))
        }
    }
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList | Out-Null
        exit 0
    } catch {
        Write-Host 'UAC reddedildi. Dosyaya sag tik -> "Yonetici olarak calistir".' -ForegroundColor Red
        exit 3
    }
}
if (-not (Test-WarpAdmin)) {
    Write-Host 'Bu kurulum yonetici haklari gerektirir.' -ForegroundColor Red
    exit 3
}

Initialize-WarpTls
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null }
# Experimental: extra processes the user wants behind the same tunnel. The GUI
# always passes this switch (an empty value clears the list); the .bat passes
# nothing and leaves whatever is there alone.
if ($null -ne $ExtraApps) {
    $names = @($ExtraApps -split '[,;]' |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { $_ -match '^[a-z0-9._-]+\.exe$' } |
        Select-Object -Unique)
    Write-WarpJsonFile (Join-Path $InstallDir 'extra-apps.json') ([ordered]@{ processes = @($names) })
    if ($names.Count -gt 0) { Write-WarpLog ('ek surecler: ' + ($names -join ', ')) 'OK' }
}
Initialize-WarpLog -Path (Join-Path $InstallDir 'install.log') -Title 'WarpCord install'

# --------------------------------------------------- host hardening (v3) --
# Actions run here (before any download) but the report is printed after the
# banner, so the console still opens with the product header.
try { Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue } catch { }
$script:HostTrust = @()
$posture = Get-WarpSecurityPosture
if ($posture.LanguageMode -ne 'FullLanguage') {
    Write-WarpLog 'PowerShell kisitli dil modunda (ConstrainedLanguage): betikler calismaz.' 'ERROR'
    Write-WarpLog 'Ipucu: WDAC/AppLocker politikasini kaldirin veya tam yetkili bir hesapta calistirin.' 'WARN'
    exit 9
}
$motw = Clear-WarpMotw -Path $RepoDir
$trust = [pscustomobject]@{ Applied = @(); Failed = @(); Reason = '' }
if (-not $NoSecurityChanges) {
    $trust = Set-WarpHostTrust -Paths @($RepoDir, $InstallDir) -Processes @('sing-box.exe', 'warp-frag.exe')
    $script:HostTrust = @($trust.Applied)
}
$script:HostReport = [pscustomobject]@{ Posture = $posture; Motw = $motw; Trust = $trust }

$script:RuntimeStopped = $false
$stagingDir = $null
$changedDns = $false
# ------------------------------------------------------------- functions --
function Stop-WarpRuntime {
    foreach ($task in @($script:WarpCordTaskName, $script:WarpCordFragTask, $script:WarpCordHealTask)) {
        try { Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue } catch { }
        try { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue } catch { }
    }
    Get-Process -Name 'sing-box', 'warp-frag' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $script:RuntimeStopped = $true
}

function Exit-WarpFail {
    param([int]$Code, [string]$Message = '')
    if ($Message) {
        Write-WarpLog $Message 'ERROR'
        $hint = Get-WarpErrorHint -Message $Message
        if ($hint) { Write-WarpLog "Ipucu: $hint" 'WARN' }
        Write-WarpLog 'Ayrintili tani icin: tani.bat' 'INFO'
    }
    if ($script:RuntimeStopped) {
        # A stopped tunnel plus hijacked DNS would leave the PC without DNS.
        $backupPath = Join-Path $InstallDir 'dns-backup.json'
        if ($changedDns -or (Test-Path $backupPath)) {
            Write-WarpLog 'DNS eski haline donduruluyor (internet erisimi korunuyor)' 'WARN'
            Restore-WarpDns -InstallDir $InstallDir
            $changedDns = $false
        }
        Stop-WarpRuntime
    }
    if ($stagingDir -and (Test-Path $stagingDir)) { Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host ''
    Write-WarpLog "Ayrinti: $(Join-Path $InstallDir 'install.log')" 'INFO'
    exit $Code
}

function Invoke-WarpDownload {
    param([string]$Uri, [string]$OutFile, [int]$Attempts = 3, [int]$TimeoutSec = 180)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            if (Test-Path $OutFile) { Remove-Item $OutFile -Force -ErrorAction SilentlyContinue }
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing -TimeoutSec $TimeoutSec -ErrorAction Stop
            if ((Test-Path $OutFile) -and (Get-Item $OutFile).Length -gt 0) { return $true }
            Write-WarpLog "indirilen dosya bos: $Uri" 'WARN'
        } catch {
            Write-WarpLog "indirme basarisiz ($attempt/$Attempts): $($_.Exception.Message)" 'WARN'
        }
        Start-Sleep -Seconds (2 * $attempt)
    }
    return $false
}

function Expand-WarpArchive {
    param([string]$Archive, [string]$Destination)
    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    try {
        Expand-Archive -Path $Archive -DestinationPath $Destination -Force -ErrorAction Stop
    } catch {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $Destination)
    }
}

function Test-SingBoxBinary {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $false }
    try {
        $output = & $Path version 2>&1
        return (($output -join "`n") -match 'sing-box version')
    } catch {
        return $false
    }
}

function Get-SingBoxStaged {
    param([string]$Arch, [string]$Staging)
    $candidates = @(
        (Join-Path $InstallDir 'sing-box.exe'),
        (Join-Path (Get-WarpCacheDir $InstallDir) 'sing-box.exe'),
        (Join-Path $RepoDir 'bin\sing-box.exe'),
        (Join-Path $RepoDir 'sing-box.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-SingBoxBinary $candidate) {
            Write-WarpLog "mevcut sing-box kullaniliyor: $candidate" 'OK'
            return $candidate
        }
    }

    $zip = Join-Path $Staging 'sing-box.zip'
    $urls = New-Object System.Collections.ArrayList
    [void]$urls.Add("https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-$Arch.zip")
    try {
        $latest = Invoke-RestMethod -Uri 'https://api.github.com/repos/SagerNet/sing-box/releases/latest' `
            -Headers @{ 'User-Agent' = 'WarpCord' } -TimeoutSec 30 -UseBasicParsing
        if ($latest -and $latest.tag_name) {
            $tag = [string]$latest.tag_name
            $version = $tag.TrimStart('v')
            $latestUrl = "https://github.com/SagerNet/sing-box/releases/download/$tag/sing-box-$version-windows-$Arch.zip"
            if (-not $urls.Contains($latestUrl)) { [void]$urls.Add($latestUrl) }
        }
    } catch { }

    foreach ($url in $urls) {
        Write-WarpLog "sing-box indiriliyor: $url" 'STEP'
        if (-not (Invoke-WarpDownload -Uri $url -OutFile $zip)) { continue }
        try {
            $extract = Join-Path $Staging 'sing-box-x'
            Expand-WarpArchive -Archive $zip -Destination $extract
            $exe = Get-ChildItem -Path $extract -Recurse -Filter 'sing-box.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $exe) { throw 'arsivde sing-box.exe yok' }
            $target = Join-Path $Staging 'sing-box.exe'
            Copy-Item $exe.FullName $target -Force
            if (Test-SingBoxBinary $target) {
                Write-WarpLog 'sing-box indirildi' 'OK'
                Save-WarpCache -InstallDir $InstallDir -Name 'sing-box.exe' -Source $target
                return $target
            }
            throw 'indirilen sing-box calismiyor'
        } catch {
            Write-WarpLog "arsiv hatasi: $($_.Exception.Message)" 'WARN'
        }
    }
    return $null
}

function Get-WintunStaged {
    param([string]$Arch, [string]$Staging)
    $installed = Join-Path $InstallDir 'wintun.dll'
    if (Test-Path $installed) { Write-WarpLog 'wintun.dll kurulu' 'OK'; return $installed }
    $cachedDll = Join-Path (Get-WarpCacheDir $InstallDir) 'wintun.dll'
    if (Test-Path $cachedDll) { Write-WarpLog 'wintun.dll onbellekten kullanilacak' 'OK'; return $cachedDll }
    foreach ($local in @((Join-Path $RepoDir "bin\wintun-$Arch.dll"), (Join-Path $RepoDir 'bin\wintun.dll'))) {
        if (Test-Path $local) { Write-WarpLog "wintun.dll yerel kopyadan kullanilacak: $local" 'OK'; return $local }
    }
    $systemDll = Join-Path $env:SystemRoot 'System32\wintun.dll'
    if (Test-Path $systemDll) { Write-WarpLog 'wintun.dll System32 icinden kullanilacak' 'OK'; return $systemDll }

    $zip = Join-Path $Staging 'wintun.zip'
    $url = "https://www.wintun.net/builds/wintun-$WintunVersion.zip"
    Write-WarpLog 'wintun.dll indiriliyor' 'STEP'
    if (Invoke-WarpDownload -Uri $url -OutFile $zip) {
        try {
            $extract = Join-Path $Staging 'wintun-x'
            Expand-WarpArchive -Archive $zip -Destination $extract
            $dll = Join-Path $extract "wintun\bin\$Arch\wintun.dll"
            if (-not (Test-Path $dll)) {
                $dll = (Get-ChildItem -Path $extract -Recurse -Filter 'wintun.dll' -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName -match "\\$Arch\\" } | Select-Object -First 1).FullName
            }
            if ($dll -and (Test-Path $dll)) {
                $target = Join-Path $Staging 'wintun.dll'
                Copy-Item $dll $target -Force
                Write-WarpLog "wintun.dll indirildi ($Arch)" 'OK'
                Save-WarpCache -InstallDir $InstallDir -Name 'wintun.dll' -Source $target
                return $target
            }
        } catch {
            Write-WarpLog "wintun hatasi: $($_.Exception.Message)" 'WARN'
        }
    }
    return $null
}

function Register-WarpTask {
    param([string]$Name, [string]$Executable, [string]$Arguments, [string]$WorkingDirectory)
    $action = New-ScheduledTaskAction -Execute $Executable -Argument $Arguments -WorkingDirectory $WorkingDirectory
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Description 'WarpCord - Discord-only tunnel' -Force | Out-Null
}

function Wait-WarpTun {
    param([int]$TimeoutSeconds = 30)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $adapter = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
        if ($adapter -and $adapter.Status -eq 'Up') { return $adapter }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

# =========================================================================== #
Write-Host ''
Write-Host '  W A R P C O R D' -ForegroundColor Cyan
Write-Host "  Discord-only tunnel  v$script:WarpCordVersion  -  Windows 10/11" -ForegroundColor DarkGray

# ------------------------------------------------------------- preflight ---
Write-WarpSection 'Sistem hazirligi'
$posture = $script:HostReport.Posture
Write-WarpLog ("PowerShell {0} | dil modu {1} | politika {2}" -f $PSVersionTable.PSVersion, $posture.LanguageMode, $posture.ExecutionPolicy) 'INFO'
if ($posture.WdacEnforced) { Write-WarpLog 'WDAC/UMCI zorlamasi etkin: imzasiz betik ve ikililer engellenebilir.' 'WARN' }
if ($posture.SmartAppControl -eq 'on') { Write-WarpLog 'Smart App Control acik: imzasiz sing-box/warp-frag engellenebilir (tek cozum SAC kapatmak).' 'WARN' }
if ($posture.PolicyEnforcedByGpo) { Write-WarpLog 'ExecutionPolicy bir GPO ile zorlaniyor; Bypass calismazsa imza gerekir.' 'WARN' }
if ($script:HostReport.Motw -gt 0) { Write-WarpLog ("{0} dosyadan internet isareti (MOTW) kaldirildi" -f $script:HostReport.Motw) 'OK' }
Write-WarpLog ("Defender: {0} | gercek zamanli: {1} | CFA: {2}" -f $(if ($posture.DefenderPresent) { 'var' } else { 'yok' }), $posture.DefenderRealTime, $posture.ControlledFolderAccess) 'INFO'
if (-not $NoSecurityChanges) {
    $t = $script:HostReport.Trust
    if ($t.Applied.Count -gt 0) { Write-WarpLog ("host guven ayarlari uygulandi ({0})" -f $t.Applied.Count) 'OK' }
    if ($t.Failed.Count -gt 0) { Write-WarpLog ("bazi guven ayarlari uygulanamadi: {0}" -f ($t.Failed -join '; ')) 'WARN' }
} else {
    Write-WarpLog 'guvenlik istisnalari atlandi (-NoSecurityChanges)' 'INFO'
}

Write-WarpSection 'Sistem kontrolu'
$arch = Get-WarpArchitecture
Write-WarpLog "Windows $([Environment]::OSVersion.Version) - $arch - PowerShell $($PSVersionTable.PSVersion)" 'INFO'
if (-not [Environment]::Is64BitOperatingSystem) {
    Write-WarpLog 'WarpCord 64-bit Windows gerektirir (sing-box 64-bit).' 'ERROR'
    exit 4
}
if ($arch -ne 'amd64' -and $arch -ne 'arm64') {
    Write-WarpLog "desteklenmeyen mimari: $arch" 'ERROR'
    exit 4
}

$defaultAdapter = Get-WarpPhysicalAdapter
if ($defaultAdapter) {
    Write-WarpLog "varsayilan baglanti: $($defaultAdapter.Alias) - $($defaultAdapter.Description)" 'INFO'
    if ($defaultAdapter.Description -match 'VPN|WireGuard|OpenVPN|TAP-|Tunnel|Tailscale|ZeroTier|WARP') {
        Write-WarpLog 'Varsayilan baglanti bir VPN gibi gorunuyor. WarpCord ile birlikte baska bir VPN calistirmayin.' 'WARN'
    }
} else {
    Write-WarpLog 'Varsayilan ag rotasi yok (kablo/Wi-Fi baglantisi kopuk olabilir).' 'WARN'
}

# ---------------------------------------------------------------- internet --
Write-WarpSection 'Internet'
$connectivity = Invoke-WarpTlsProbe -HostName 'example.com' -Path '/' -Expect 'Example Domain' -TimeoutSec 10
if (-not $connectivity.Ok) {
    # A previous install may have left DNS hijacked with no tunnel running;
    # repair that first instead of refusing to install.
    if (Test-Path (Join-Path $InstallDir 'dns-backup.json')) {
        Write-WarpLog 'Internet erisimi yok; onceki kurulumun DNS ayari geri alinip yeniden denenecek.' 'WARN'
        Restore-WarpDns -InstallDir $InstallDir
        Start-Sleep -Seconds 1
        $connectivity = Invoke-WarpTlsProbe -HostName 'example.com' -Path '/' -Expect 'Example Domain' -TimeoutSec 10
    }
}
if (-not $connectivity.Ok) {
    Exit-WarpFail -Code 8 -Message 'Internet baglantisi yok veya giris sayfasi (captive portal) acik. Baglantiyi duzeltip WarpCord.bat dosyasini tekrar calistirin.'
}
Write-WarpLog "internet OK ($($connectivity.Ms) ms)" 'OK'

$stagingDir = Join-Path $env:TEMP ("warpcord-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

# ---------------------------------------------------------- prerequisites ---
Write-WarpSection 'Bilesenler (mevcut tunel bozulmadan hazirlanir)'
$stagedSingBox = Get-SingBoxStaged -Arch $arch -Staging $stagingDir
if (-not $stagedSingBox) {
    Exit-WarpFail -Code 5 -Message 'sing-box indirilemedi. Internet baglantisini kontrol edin.'
}
$stagedWintun = Get-WintunStaged -Arch $arch -Staging $stagingDir
if (-not $stagedWintun) {
    Exit-WarpFail -Code 5 -Message 'wintun.dll olmadan TUN acilamaz. wintun.net adresinden indirip tekrar deneyin.'
}

$fragSource = Get-WarpFragSource -ScriptRoot $RepoDir
$stagedFrag = Join-Path $stagingDir 'warp-frag.exe'
$fragOk = $false
if ($fragSource) {
    Write-WarpLog 'DPI yedek modu derleniyor (TLS parcalama)...' 'INFO'
    $fragOk = Build-WarpFrag -SourcePath $fragSource -OutputPath $stagedFrag
    if (-not $fragOk) { Write-WarpLog 'DPI yedek modu kullanilamiyor (derleme hatasi).' 'WARN' }
} else {
    Write-WarpLog 'tools\warpfrag\WarpFrag.cs bulunamadi; DPI yedek modu yok.' 'WARN'
}

# --------------------------------------------------------------- account ---
Write-WarpSection 'WARP hesabi'
$accountPath = Join-Path $InstallDir 'account.json'
$account = $null
if ($Mode -eq 'dpi') {
    Write-WarpLog 'DPI modu: WARP kaydi gerekmiyor' 'INFO'
} else {
    if (-not $Force -and (Test-Path $accountPath)) {
        $candidate = Read-WarpJsonFile $accountPath
        if ($candidate -and $candidate.PrivateKey -and $candidate.PeerPublicKey) {
            $account = $candidate
            Write-WarpLog "mevcut WARP cihazi kullaniliyor ($($account.AddressV4))" 'OK'
        }
    }
    if (-not $account) {
        $legacyConfig = Join-Path $RepoDir 'config.json'
        if (Test-Path $legacyConfig) {
            $account = Get-WarpAccountFromConfig -ConfigPath $legacyConfig
            if ($account) { Write-WarpLog 'config.json icindeki cihaz yeniden kullaniliyor' 'OK' }
        }
    }
    if (-not $account) {
        Write-WarpLog 'ucretsiz WARP cihazi kaydediliyor...' 'STEP'
        $account = New-WarpRegistration
    }
    if (-not $account -and $fragOk) {
        # The registration API is a Cloudflare host too; if the ISP filters it,
        # bootstrap registration through the fragmenting proxy.
        Write-WarpLog 'kayit dogrudan yapilamadi, DPI proxy uzerinden deneniyor...' 'WARN'
        $regPort = Get-FreeTcpPort
        $regProcess = $null
        try {
            $regProcess = Start-Process -FilePath $stagedFrag -ArgumentList @('--port', "$regPort", '--mode', 'tlsrec') -PassThru -WindowStyle Hidden
            for ($i = 0; $i -lt 30; $i++) { if (Test-TcpPort -Port $regPort -TimeoutMs 200) { break }; Start-Sleep -Milliseconds 100 }
            $account = New-WarpRegistration -Proxy ("http://127.0.0.1:$regPort")
        } finally {
            if ($regProcess -and -not $regProcess.HasExited) {
                try { $regProcess.Kill(); [void]$regProcess.WaitForExit(2000) } catch { }
            }
        }
    }
    if (-not $account) {
        Write-WarpLog 'WARP cihazi kaydedilemedi; DPI moduna gecilecek.' 'WARN'
    }
}

# ------------------------------------------------------------------ mode ---
Write-WarpSection 'Mod secimi'
$chosenMode = $Mode
$selectedEndpoint = $null
$fragPort = 1081

if ($chosenMode -eq 'auto') {
    if ($SkipProbe -and $account) {
        $chosenMode = 'warp'
        $state = Get-WarpState $InstallDir
        if ($state -and $state.Endpoint -and $state.Endpoint.Address) {
            $selectedEndpoint = [pscustomobject]@{ Address = $state.Endpoint.Address; Port = [int]$state.Endpoint.Port; Label = 'kayitli' }
        } else {
            $selectedEndpoint = [pscustomobject]@{ Address = $account.EndpointV4; Port = 2408; Label = 'varsayilan' }
        }
        Write-WarpLog "probe atlandi: $($selectedEndpoint.Address):$($selectedEndpoint.Port)" 'INFO'
    } elseif ($account) {
        if ($Endpoint) {
            $parts = $Endpoint.Split(':')
            $selectedEndpoint = [pscustomobject]@{ Address = $parts[0]; Port = [int]$parts[1]; Label = 'manuel' }
        } else {
            $selectedEndpoint = Select-WarpEndpoint -SingBoxPath $stagedSingBox -Account $account -WorkDir $stagingDir -BudgetSeconds 90
        }
        $chosenMode = if ($selectedEndpoint) { 'warp' } else { 'dpi' }
        if (-not $selectedEndpoint -and (Test-WarpIpv6Disabled)) {
            Write-WarpLog 'Bu makinede IPv6 devre disi (DisabledComponents / ms_tcpip6).' 'WARN'
            Write-WarpLog 'sing-box WARP modunda IPv6 soketini ag adaptorune baglayamiyor; WARP bu yuzden kullanilamaz.' 'WARN'
            Write-WarpLog 'Discord icin DPI motoru kullanilacak (calisir, ama IP gizlenmez). WARP icin IPv6 yi acin.' 'INFO'
        } elseif (-not $selectedEndpoint) {
            Write-WarpLog 'WARP bu hatta erisilemedi; DPI motoruna geciliyor (Discord acilir, IP gizlenmez).' 'INFO'
        }
    } else {
        $chosenMode = 'dpi'
    }
}

if ($chosenMode -eq 'warp') {
    if (-not $account) {
        Exit-WarpFail -Code 6 -Message 'WARP modu icin cihaz kaydi gerekiyor (-Mode dpi deneyin).'
    }
    if (-not $selectedEndpoint -and $Endpoint) {
        $parts = $Endpoint.Split(':')
        $selectedEndpoint = [pscustomobject]@{ Address = $parts[0]; Port = [int]$parts[1]; Label = 'manuel' }
    }
    if (-not $selectedEndpoint) {
        $selectedEndpoint = Select-WarpEndpoint -SingBoxPath $stagedSingBox -Account $account -WorkDir $stagingDir -BudgetSeconds 90
    }
    if (-not $selectedEndpoint) {
        if (Test-WarpIpv6Disabled) {
            Exit-WarpFail -Code 7 -Message 'WARP bu hatta erisilebilir degil: IPv6 devre disi oldugu icin sing-box WARP soketini baglayamiyor. "-Mode auto" veya "-Mode dpi" kullanin.'
        }
        Exit-WarpFail -Code 7 -Message 'WARP bu hatta erisilebilir degil. "WarpCord.bat -Mode auto" veya "-Mode dpi" deneyin.'
    }
    Write-WarpLog "mod: WARP tuneli -> $($selectedEndpoint.Address):$($selectedEndpoint.Port)" 'STEP'
    $config = New-WarpTunnelConfig -Account $account -Endpoint $selectedEndpoint -LogPath (Join-Path $InstallDir 'tunnel.log')
} else {
    if (-not $fragOk) {
        Exit-WarpFail -Code 6 -Message 'DPI modu secildi ama parcalama proxy derlenemedi.'
    }
    $fragPort = Get-FreeTcpPort -Preferred 1081
    $fragTest = Test-WarpFrag -FragPath $stagedFrag -Port $fragPort -LogPath (Join-Path $stagingDir 'frag-test.log')
    if (-not $fragTest.Ok) {
        Exit-WarpFail -Code 6 -Message "DPI modu testi basarisiz: $($fragTest.Detail)"
    }
    Write-WarpLog "mod: DPI (TLS parcalama) - proxy port $fragPort, $($fragTest.Ms) ms" 'STEP'
    $config = New-WarpDpiConfig -LogPath (Join-Path $InstallDir 'tunnel.log') -FragPort $fragPort
}

# --------------------------------------------------------------- install ---
Write-WarpSection 'Kurulum (eski tunel simdi durduruluyor)'
Stop-WarpRuntime

$singBoxPath = Join-Path $InstallDir 'sing-box.exe'
$fragPath = Join-Path $InstallDir 'warp-frag.exe'
$wintunPath = Join-Path $InstallDir 'wintun.dll'
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Get-Process -Name 'sing-box', 'warp-frag' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        # Repair runs stage the installed copy itself (see Get-SingBoxStaged /
        # Get-WintunStaged); copying a file onto itself aborts the install.
        if ([IO.Path]::GetFullPath($stagedSingBox) -ne [IO.Path]::GetFullPath($singBoxPath)) {
            Copy-Item $stagedSingBox $singBoxPath -Force -ErrorAction Stop
        }
        if ([IO.Path]::GetFullPath($stagedWintun) -ne [IO.Path]::GetFullPath($wintunPath)) {
            Copy-Item $stagedWintun $wintunPath -Force -ErrorAction Stop
        }
        if ($fragOk) { Copy-Item $stagedFrag $fragPath -Force -ErrorAction Stop }
        break
    } catch {
        if ($attempt -eq 5) {
            Exit-WarpFail -Code 5 -Message "dosyalar kopyalanamadi: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 1
    }
}
Write-WarpLog 'binaries kuruldu (sing-box + wintun + DPI proxy)' 'OK'

$configPath = Join-Path $InstallDir 'config.json'
Write-WarpJsonFile $configPath $config
$checkOutput = & $singBoxPath check -c $configPath 2>&1
if ($LASTEXITCODE -ne 0) {
    Exit-WarpFail -Code 6 -Message "config dogrulanamadi: $($checkOutput -join ' ')"
}
Write-WarpLog 'config dogrulandi (sing-box check)' 'OK'
if ($account) {
    Write-WarpJsonFile $accountPath $account
    Set-WarpPrivateAcl $accountPath
}
Set-WarpPrivateAcl $configPath

$tunnelLog = Join-Path $InstallDir 'tunnel.log'
foreach ($logCandidate in @($tunnelLog, (Join-Path $InstallDir 'frag.log'))) {
    if (Test-Path $logCandidate) {
        try { if ((Get-Item $logCandidate).Length -gt 10MB) { Move-Item $logCandidate "$logCandidate.old" -Force } } catch { }
    }
}

foreach ($task in @($script:WarpCordTaskName, $script:WarpCordFragTask, $script:WarpCordHealTask)) {
    try { Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue } catch { }
}
& netsh advfirewall firewall delete rule name="$($script:WarpCordLegacyFirewall)" 2>&1 | Out-Null
& netsh advfirewall firewall delete rule name="$($script:WarpCordFirewall)" 2>&1 | Out-Null
foreach ($program in @($singBoxPath, $fragPath)) {
    if (-not (Test-Path $program)) { continue }
    $ruleName = "WarpCord $([IO.Path]::GetFileName($program))"
    try {
        New-NetFirewallRule -DisplayName $ruleName -Direction Outbound -Program $program `
            -Action Allow -Profile Any -ErrorAction Stop | Out-Null
    } catch {
        & netsh advfirewall firewall add rule name="$ruleName" dir=out program="$program" action=allow 2>&1 | Out-Null
    }
}
Write-WarpLog 'guvenlik duvari izinleri eklendi' 'OK'

# DNS is only touched when the ISP poisons Discord names: a split tunnel
# cannot fix poisoned answers, and nothing else about the system needs to move.
$dnsBefore = Test-DiscordDns
Write-WarpLog "DNS discord.com -> $($dnsBefore.Detail)" 'INFO'
if ($KeepDns) {
    Write-WarpLog 'DNS degistirilmedi (-KeepDns)' 'INFO'
} elseif ($dnsBefore.Blocked -or -not $dnsBefore.Ok) {
    $backup = Backup-WarpDns -InstallDir $InstallDir
    if ($backup -and (Set-WarpDns -Backup $backup -IncludeV6)) {
        $changedDns = $true
        Write-WarpLog 'adaptor DNS 1.1.1.1 yapildi (geri alma yedegi var)' 'OK'
    } else {
        # Nothing was changed, so the backup is misleading: drop it.
        Remove-Item (Join-Path $InstallDir 'dns-backup.json') -Force -ErrorAction SilentlyContinue
        Write-WarpLog 'adaptor DNS ayarlanamadi; zehirli cevaplar sorun cikarabilir' 'WARN'
    }
} else {
    Write-WarpLog 'DNS zaten temiz, sistem DNS ayarlarina dokunulmadi' 'OK'
}

if ($chosenMode -eq 'dpi') {
    Register-WarpTask -Name $script:WarpCordFragTask -Executable $fragPath `
        -Arguments ("--port {0} --mode tlsrec --delay 3 --log `"{1}`"" -f $fragPort, (Join-Path $InstallDir 'frag.log')) `
        -WorkingDirectory $InstallDir
    Write-WarpLog 'parcalama proxy gorevi olusturuldu' 'OK'
    Start-ScheduledTask -TaskName $script:WarpCordFragTask
    for ($i = 0; $i -lt 40; $i++) { if (Test-TcpPort -Port $fragPort -TimeoutMs 200) { break }; Start-Sleep -Milliseconds 250 }
    if (-not (Test-TcpPort -Port $fragPort -TimeoutMs 500)) {
        Write-WarpLog 'parcalama proxy baslamadi' 'WARN'
    }
}

Register-WarpTask -Name $script:WarpCordTaskName -Executable $singBoxPath `
    -Arguments ("run -c `"{0}`"" -f $configPath) -WorkingDirectory $InstallDir
Write-WarpLog 'tunel gorevi olusturuldu (SYSTEM, acilista, hata halinde yeniden baslar)' 'OK'
Start-ScheduledTask -TaskName $script:WarpCordTaskName

$tun = Wait-WarpTun -TimeoutSeconds 30
if (-not $tun) { Write-WarpLog 'TUN adaptoru 30 saniye icinde acilmadi' 'WARN' }

$endpointState = $null
if ($chosenMode -eq 'warp' -and $selectedEndpoint) {
    $endpointState = [ordered]@{ Address = $selectedEndpoint.Address; Port = [int]$selectedEndpoint.Port }
}
$fragPortState = $null
if ($chosenMode -eq 'dpi') { $fragPortState = $fragPort }
$state = [ordered]@{
    version        = $script:WarpCordVersion
    mode           = $chosenMode
    endpoint       = $endpointState
    fragPort       = $fragPortState
    changedDns     = $changedDns
    installedAt    = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    arch           = $arch
    repoPath       = $RepoDir
    singBoxVersion = $SingBoxVersion
    wintunVersion  = $WintunVersion
    hostTrust      = @($script:HostTrust)
}
Save-WarpState -InstallDir $InstallDir -State $state

# ---------------------------------------------------------------- verify ---
Write-WarpSection 'Dogrulama'
Start-Sleep -Seconds 2
Clear-WarpDnsCache
Start-Sleep -Milliseconds 500

$checks = [ordered]@{}
$singBoxProcess = Get-Process -Name 'sing-box' -ErrorAction SilentlyContinue
$checks['sing-box'] = [bool]$singBoxProcess
Write-WarpLog ("sing-box: " + $(if ($singBoxProcess) { "CALISIYOR (pid $($singBoxProcess[0].Id))" } else { 'CALISMIYOR' })) $(if ($singBoxProcess) { 'OK' } else { 'ERROR' })

if ($chosenMode -eq 'dpi') {
    $fragProcess = Get-Process -Name 'warp-frag' -ErrorAction SilentlyContinue
    $checks['frag'] = [bool]$fragProcess
    Write-WarpLog ("parcalama proxy: " + $(if ($fragProcess) { 'CALISIYOR' } else { 'CALISMIYOR' })) $(if ($fragProcess) { 'OK' } else { 'ERROR' })
}

$adapter = Get-NetAdapter -Name $script:WarpCordTunName -ErrorAction SilentlyContinue
$checks['tun'] = [bool]($adapter -and $adapter.Status -eq 'Up')
Write-WarpLog ("TUN: " + $(if ($adapter) { "$($adapter.Name) [$($adapter.Status)]" } else { 'YOK' })) $(if ($checks['tun']) { 'OK' } else { 'WARN' })

$dns = Test-DiscordDns
$checks['dns'] = [bool]$dns.Ok
Write-WarpLog "DNS discord.com -> $($dns.Detail)" $(if ($dns.Ok) { 'OK' } else { 'ERROR' })

$api = Test-DiscordApi
$checks['api'] = [bool]$api.Ok
$detail = $api.Detail
if ($detail.Length -gt 100) { $detail = $detail.Substring(0, 100) + '...' }
$msText = if ($api.Ms -ge 0) { " ($($api.Ms) ms)" } else { '' }
Write-WarpLog "Discord API$msText -> $detail" $(if ($api.Ok) { 'OK' } else { 'ERROR' })

$otherOk = $false
try {
    $other = Invoke-WebRequest -Uri 'https://example.com/' -UseBasicParsing -TimeoutSec 15
    $otherOk = $other.StatusCode -eq 200
} catch { }
$checks['direct'] = $otherOk
Write-WarpLog "Discord disi trafik (example.com): $(if ($otherOk) { 'OK' } else { 'BOZUK' })" $(if ($otherOk) { 'OK' } else { 'ERROR' })

$publicIp = Get-PublicIp
if ($publicIp) { Write-WarpLog "dis IP (Discord disi trafik): $publicIp" 'INFO' }

$logTail = @()
try { $logTail = @(Get-Content -Path $tunnelLog -Tail 60 -ErrorAction SilentlyContinue) } catch { }
$tunnelHits = @($logTail | Where-Object { $_ -match 'wireguard\[warp\]|http\[frag\]' }).Count
$directHits = @($logTail | Where-Object { $_ -match 'outbound/direct' }).Count
Write-WarpLog "log: tunel akisi $tunnelHits satir, direkt akis $directHits satir" 'INFO'

$healthy = $checks['sing-box'] -and $checks['dns'] -and $checks['api']
if ($healthy -and $chosenMode -eq 'dpi') { $healthy = $checks['frag'] }

# -------------------------------------------------------------- guardian ---
if ($healthy) {
    $modeLabel = if ($chosenMode -eq 'warp') {
        "WARP tuneli ($($selectedEndpoint.Address):$($selectedEndpoint.Port))"
    } else {
        'DPI modu (TLS parcalama)'
    }

    # ------------------------------------------------------------ guardian --
    # v2.5: everything below runs on its own from now on. Errors are written to
    # heal.log in the install dir and repaired without the user doing anything.
    Write-WarpSection 'Koruyucu (otomatik onarim)'
    $guardDir = Join-Path $InstallDir 'tools'
    New-Item -ItemType Directory -Force -Path $guardDir | Out-Null
    foreach ($guardFile in @('warpcore.ps1', 'warpheal.ps1')) {
        $guardSource = Join-Path $RepoDir "tools\$guardFile"
        if (Test-Path $guardSource) { Copy-Item $guardSource (Join-Path $guardDir $guardFile) -Force }
    }
    $guardScript = Join-Path $guardDir 'warpheal.ps1'
    if (Test-Path $guardScript) {
        try {
            $healAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
                -Argument ("-NoProfile -ExecutionPolicy Bypass -File `"{0}`" -Quiet -NoElevate" -f $guardScript) `
                -WorkingDirectory $InstallDir
            $healTrigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(2)) `
                -RepetitionInterval (New-TimeSpan -Minutes $script:WarpCordHealEvery)
            $healPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
            $healSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName $script:WarpCordHealTask -Action $healAction -Trigger $healTrigger `
                -Principal $healPrincipal -Settings $healSettings `
                -Description 'WarpCord guardian - checks and repairs the tunnel every 5 minutes' -Force | Out-Null
            Write-WarpLog "koruyucu gorev kuruldu: $($script:WarpCordHealTask) (her $($script:WarpCordHealEvery) dakika)" 'OK'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $guardScript -NoElevate -Quiet 2>&1 | Out-Null
            Write-WarpLog ("koruyucu ilk kontrol: " + $(if ($LASTEXITCODE -eq 0) { 'saglikli' } else { "kod $LASTEXITCODE" })) 'INFO'
        } catch {
            Write-WarpLog "koruyucu gorev kurulamadi: $($_.Exception.Message)" 'WARN'
        }
    } else {
        Write-WarpLog 'tools\warpheal.ps1 bulunamadi; koruyucu kurulmadi' 'WARN'
    }

    Write-WarpSection 'Sonuc'
    if ($stagingDir -and (Test-Path $stagingDir)) { Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
    Write-WarpLog "KURULUM BASARILI - $modeLabel" 'OK'
    Write-Host ''
    Write-Host '  Discord artik acilmali. Son adim:' -ForegroundColor White
    Write-Host '  Discord uygulamasini tamamen kapatip (tepsi simgesinden de) yeniden acin.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Durum  : durum.bat' -ForegroundColor Gray
    Write-Host '  Onarim : onar.bat (koruyucu her 5 dakikada bir kendisi de kontrol eder)' -ForegroundColor Gray
    Write-Host '  Kaldir : kaldir.bat' -ForegroundColor Gray
    Write-Host "  Log    : $tunnelLog" -ForegroundColor Gray
    if ($changedDns) {
        Write-Host '  Not    : Adaptor DNS 1.1.1.1 yapildi; kaldir.bat geri alir.' -ForegroundColor DarkGray
    }
    exit 0
} else {
    Write-WarpLog 'KURULUM TAMAMLANMADI - dogrulama basarisiz' 'ERROR'
    Write-WarpLog 'Son 25 log satiri:' 'INFO'
    try {
        Get-Content -Path $tunnelLog -Tail 25 -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    } catch { }
    Exit-WarpFail -Code 2 -Message 'Dogrulama basarisiz; tunel durduruldu ve DNS geri alindi.'
}
