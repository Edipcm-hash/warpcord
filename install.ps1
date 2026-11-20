#Requires -RunAsAdministrator
# WarpCord installer — fetches sing-box, registers a WARP device if needed,
# installs a SYSTEM-level scheduled task and verifies the result.
param(
    [string]$SourceDir = $PSScriptRoot,
    [string]$SingBoxVersion = '1.14.4'
)

$ErrorActionPreference = 'Continue'
$InstallDir = 'C:\ProgramData\DiscordTunnel'
$TaskName   = 'DiscordOnlyTunnel'
$Log        = Join-Path $InstallDir 'install.log'

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Set-Content -Path $Log -Value "=== WarpCord install $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
function Log($m) {
    $line = "$(Get-Date -Format 'HH:mm:ss')  $m"
    Write-Host $line
    Add-Content -Path $Log -Value $line
}

Log "source: $SourceDir"

# ---------------------------------------------------------------- config ---
$configPath = Join-Path $SourceDir 'config.json'
if (-not (Test-Path $configPath)) {
    Log 'config.json yok, WARP cihazi kaydediliyor...'
    $py = $null
    foreach ($cand in 'py', 'python', 'python3') {
        $cmd = Get-Command $cand -ErrorAction SilentlyContinue
        if ($cmd) { $py = $cmd.Source; break }
    }
    if (-not $py) {
        Log 'HATA: Python bulunamadi. https://python.org kurup register.bat calistir.'
        exit 1
    }
    $regScript = Join-Path $SourceDir 'tools\warp-register.py'
    $args = if ((Split-Path $py -Leaf) -eq 'py.exe' -or (Split-Path $py -Leaf) -eq 'py') {
        @('-3', $regScript, '--out', $configPath, '--install-dir', $InstallDir)
    } else {
        @($regScript, '--out', $configPath, '--install-dir', $InstallDir)
    }
    & $py @args
    if (-not (Test-Path $configPath)) { Log 'HATA: config.json uretilemedi.'; exit 1 }
    Log 'config.json uretildi'
} else {
    Log 'mevcut config.json kullaniliyor'
}

# ------------------------------------------------------------ sing-box ---
$binDir = Join-Path $SourceDir 'bin'
$bin    = Join-Path $binDir 'sing-box.exe'
if (-not (Test-Path $bin)) {
    Log "sing-box v$SingBoxVersion indiriliyor..."
    $zip = Join-Path $env:TEMP "sing-box-$SingBoxVersion.zip"
    $tmp = Join-Path $env:TEMP "sing-box-$SingBoxVersion"
    $url = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-amd64.zip"
    try {
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -ErrorAction Stop
        Expand-Archive -Path $zip -DestinationPath $tmp -Force
        $exe = Get-ChildItem $tmp -Recurse -Filter 'sing-box.exe' | Select-Object -First 1
        if (-not $exe) { throw 'arsivde sing-box.exe yok' }
        New-Item -ItemType Directory -Force -Path $binDir | Out-Null
        Copy-Item $exe.FullName $bin -Force
        Log "sing-box indirildi: $bin"
    } catch {
        Log "HATA: sing-box indirilemedi: $($_.Exception.Message)"
        exit 1
    }
}

# ----------------------------------------------------------------- stop ---
# Stop any running instance before touching the files, otherwise the copy of
# sing-box.exe fails with "file is being used by another process".
try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch {}
try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue } catch {}
Get-Process sing-box -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 1200

# ---------------------------------------------------------------- files ---
foreach ($pair in @(@($bin, 'sing-box.exe'), @($configPath, 'config.json'))) {
    $target = Join-Path $InstallDir $pair[1]
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Copy-Item $pair[0] $target -Force -ErrorAction Stop
            Log "kopyalandi: $($pair[1])"
            break
        } catch {
            if ($attempt -eq 5) { Log "HATA: $($pair[1]) kopyalanamadi: $($_.Exception.Message)"; exit 1 }
            Start-Sleep -Seconds 1
        }
    }
}

# ------------------------------------------------------------ firewall ---
& netsh advfirewall firewall delete rule name="DiscordTunnel" 2>&1 | Out-Null
& netsh advfirewall firewall add rule name="DiscordTunnel" dir=out program="$InstallDir\sing-box.exe" action=allow profile=any 2>&1 | Out-Null
Log 'firewall izni eklendi'

# ---------------------------------------------------------------- dns ---
# Windows queries every configured resolver in parallel, so the ISP resolver
# keeps poisoning Discord names even while the tunnel answers correctly.
# Point the physical adapter at an off-link resolver: those packets get routed
# into the tunnel and hijacked by sing-box, which answers everything over DoH.
$defaultRoute = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
                Where-Object { $_.NextHop -ne '0.0.0.0' -and $_.InterfaceAlias -notmatch 'tun|sing' } |
                Sort-Object RouteMetric | Select-Object -First 1
$ifAlias = $defaultRoute.InterfaceAlias
if ($ifAlias) {
    # Remember whether the adapter had static DNS, so uninstall can restore it
    # instead of blindly falling back to DHCP. Never overwrite an existing
    # backup: re-running the installer must not record our own DNS as "original".
    $backupPath = Join-Path $InstallDir 'dns-backup.json'
    if (-not (Test-Path $backupPath)) {
        $staticDns = ''
        try {
            $guid = (Get-NetAdapter -Name $ifAlias -ErrorAction Stop).InterfaceGuid
            $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$guid"
            $staticDns = [string](Get-ItemProperty -Path $regPath -Name NameServer -ErrorAction SilentlyContinue).NameServer
        } catch { }
        @{ interface = $ifAlias; static = $staticDns } | ConvertTo-Json |
            Set-Content $backupPath -Encoding UTF8
        Log "DNS yedegi alindi: $ifAlias (statik: '$staticDns')"
    }
    try {
        Set-DnsClientServerAddress -InterfaceAlias $ifAlias -ServerAddresses @('1.1.1.1', '1.0.0.1') -ErrorAction Stop
        Log "DNS ayarlandi: $ifAlias -> 1.1.1.1, 1.0.0.1"
    } catch {
        Log "UYARI: adaptor DNS'i ayarlanamadi: $($_.Exception.Message)"
    }
} else {
    Log 'UYARI: varsayilan rota bulunamadi, DNS ayarlanmadi'
}

# --------------------------------------------------------------- task ---
try {
    $action    = New-ScheduledTaskAction -Execute (Join-Path $InstallDir 'sing-box.exe') `
                    -Argument "run -c `"$(Join-Path $InstallDir 'config.json')`"" -WorkingDirectory $InstallDir
    $trigger   = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
                    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force | Out-Null
    Log "gorev olusturuldu: $TaskName"
    Start-ScheduledTask -TaskName $TaskName
    Log 'gorev baslatildi'
} catch {
    Log "HATA (gorev): $($_.Exception.Message)"
}

Start-Sleep -Seconds 10

# ---------------------------------------------------------- verification ---
$proc = Get-Process sing-box -ErrorAction SilentlyContinue
Log "proses: $(if ($proc) { 'CALISIYOR pid=' + ($proc.Id -join ',') } else { 'CALISMIYOR' })"
$ad = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'tun|sing' }
Log "adaptor: $(if ($ad) { ($ad | ForEach-Object { $_.Name + '[' + $_.Status + ']' }) -join ', ' } else { 'YOK' })"

Clear-DnsClientCache -ErrorAction SilentlyContinue
& ipconfig /flushdns 2>&1 | Out-Null
Start-Sleep -Seconds 1
$ips = (Resolve-DnsName discord.com -Type A -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress) -join ', '
Log "DNS discord.com -> $ips"

$ok = $false
try {
    $tcp = New-Object Net.Sockets.TcpClient
    $tcp.Connect('discord.com', 443)
    $cb  = [Net.Security.RemoteCertificateValidationCallback] { param($a, $b, $c, $d) $true }
    $ssl = New-Object Net.Security.SslStream($tcp.GetStream(), $false, $cb)
    $ssl.AuthenticateAsClient('discord.com')
    $req = "GET /api/v9/gateway HTTP/1.1`r`nHost: discord.com`r`nUser-Agent: install-check`r`nConnection: close`r`n`r`n"
    $bytes = [Text.Encoding]::ASCII.GetBytes($req)
    $ssl.Write($bytes, 0, $bytes.Length)
    $resp = (New-Object IO.StreamReader($ssl)).ReadToEnd()
    Log "TLS+API: $($resp.Substring(0, [Math]::Min(140, $resp.Length)).Replace("`r`n", ' | '))"
    $ok = $resp -match 'gateway\.discord\.gg'
    $ssl.Close(); $tcp.Close()
} catch {
    Log "TLS+API HATA: $($_.Exception.Message)"
}
Log "SONUC: $(if ($ok) { 'BASARILI' } else { 'BASARISIZ' })"

Log '--- tunnel.log (son 15) ---'
Get-Content (Join-Path $InstallDir 'tunnel.log') -Tail 15 -ErrorAction SilentlyContinue |
    ForEach-Object { Add-Content -Path $Log -Value "    $_" }

if ($ok) { exit 0 } else { exit 2 }
