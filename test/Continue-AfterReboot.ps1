# ============================================================================
#  WarpCord - post-reboot continuation
#
#  Registered as a one-shot startup task right before the Hyper-V reboot.
#  Waits for network + Hyper-V, downloads the official Windows 11 Enterprise
#  evaluation ISO (resumable), then builds the VM and runs the clean test.
#
#  Everything is appended to D:\VMs\warpcord-vm-test.log so the result can be
#  read after the (long) unattended run.
# ============================================================================
param(
    [string]$IsoPath = 'D:\iso\Win11Eval.iso',
    [string]$LogPath = 'D:\VMs\warpcord-vm-test.log',
    [string]$IsoUrl = 'https://go.microsoft.com/fwlink/?linkid=2289031',
    [int]$IsoSizeGB = 5.02,
    [string]$GuestUser = 'Test',
    [string]$GuestPass = 'Test1234!',
    [string]$TaskName = 'WarpCordVmTest'
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot

function Log {
    param([string]$Message)
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    try {
        $dir = Split-Path -Parent $LogPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding UTF8
    } catch { }
}

Log '=== WarpCord VM testi basliyor (reboot sonrasi) ==='
Log ("Repo: {0}" -f $repo)
# One shot: remove the startup task so a later reboot cannot start a second run.
try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue } catch { }
Start-Sleep -Seconds 120   # let services settle after boot

# ------------------------------------------------------------ network wait --
Log 'Ag bekleniyor...'
$netDeadline = (Get-Date).AddMinutes(20)
$online = $false
while ((Get-Date) -lt $netDeadline) {
    try {
        $probe = [Net.HttpWebRequest]::Create('https://go.microsoft.com/fwlink/?linkid=2289031')
        $probe.Method = 'HEAD'
        $probe.Timeout = 15000
        $probe.UserAgent = 'WarpCord-Test'
        $r = $probe.GetResponse()
        $r.Close()
        $online = $true
        break
    } catch { Start-Sleep -Seconds 20 }
}
if (-not $online) { Log 'HATA: ag erisimi yok (20 dk).'; exit 2 }
Log 'Ag hazir.'

# ------------------------------------------------------------- Hyper-V wait --
Log 'Hyper-V bekleniyor...'
$hvDeadline = (Get-Date).AddMinutes(15)
$hv = $false
while ((Get-Date) -lt $hvDeadline) {
    $svc = Get-Service vmms -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') { $hv = $true; break }
    if ($svc -and $svc.Status -ne 'Running') { try { Start-Service vmms -ErrorAction SilentlyContinue } catch { } }
    Start-Sleep -Seconds 20
}
if (-not $hv) { Log 'HATA: Hyper-V (vmms) hazir degil.'; exit 2 }
Log 'Hyper-V hazir.'

# ------------------------------------------------------------- ISO download --
$isoDir = Split-Path -Parent $IsoPath
if (-not (Test-Path $isoDir)) { New-Item -ItemType Directory -Force -Path $isoDir | Out-Null }
$expected = [int64]($IsoSizeGB * 1GB)

function Get-RemoteSize {
    param([string]$Url)
    try {
        $req = [Net.HttpWebRequest]::Create($Url)
        $req.Method = 'HEAD'; $req.AllowAutoRedirect = $true; $req.Timeout = 30000
        $req.UserAgent = 'WarpCord-Test'
        $resp = $req.GetResponse()
        $len = $resp.ContentLength
        $resp.Close()
        return $len
    } catch { return -1 }
}

if (-not (Test-Path $IsoPath) -or (Get-Item $IsoPath).Length -lt $expected) {
    $remote = Get-RemoteSize $IsoUrl
    Log ("ISO indiriliyor (uzak boyut: {0} GB)..." -f [math]::Round($remote / 1GB, 2))
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            $have = 0
            if (Test-Path $IsoPath) { $have = (Get-Item $IsoPath).Length }
            if ($have -ge $expected) { break }
            $req = [Net.HttpWebRequest]::Create($IsoUrl)
            $req.Method = 'GET'; $req.AllowAutoRedirect = $true; $req.Timeout = 60000
            $req.ReadWriteTimeout = 120000; $req.UserAgent = 'WarpCord-Test'
            if ($have -gt 0) { $req.AddRange($have) }
            $resp = $req.GetResponse()
            $stream = $resp.GetResponseStream()
            $mode = if ($have -gt 0) { [IO.FileMode]::Append } else { [IO.FileMode]::Create }
            $file = New-Object IO.FileStream($IsoPath, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $buffer = New-Object byte[] (1MB)
            $lastLog = Get-Date
            while ($true) {
                $read = $stream.Read($buffer, 0, $buffer.Length)
                if ($read -le 0) { break }
                $file.Write($buffer, 0, $read)
                $have += $read
                if (((Get-Date) - $lastLog).TotalSeconds -ge 30) {
                    Log ("  ISO: {0} GB / {1} GB" -f [math]::Round($have / 1GB, 2), [math]::Round($expected / 1GB, 2))
                    $lastLog = Get-Date
                }
            }
            $file.Close(); $stream.Close(); $resp.Close()
            if ((Get-Item $IsoPath).Length -ge $expected) { break }
        } catch {
            Log ("  indirme denemesi {0}/6 basarisiz: {1}" -f $attempt, $_.Exception.Message)
            Start-Sleep -Seconds (15 * $attempt)
        }
    }
} else {
    Log 'ISO zaten indirilmis.'
}

$iso = Get-Item $IsoPath -ErrorAction SilentlyContinue
if (-not $iso -or $iso.Length -lt $expected) {
    Log ("HATA: ISO tamamlanmadi ({0} GB)." -f [math]::Round(($iso.Length / 1GB), 2))
    exit 3
}
Log ("ISO hazir: {0} GB" -f [math]::Round($iso.Length / 1GB, 2))

# ------------------------------------------------------------------- test ---
Log 'VM olusturuluyor ve test calistiriliyor...'
$runner = Join-Path $PSScriptRoot 'New-TestVm.ps1'
$out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $runner -IsoPath $IsoPath -GuestUser $GuestUser -GuestPass $GuestPass 2>&1
$code = $LASTEXITCODE
foreach ($line in $out) { Log ("  " + $line) }
Log ("=== Bitti (cikis kodu {0}) ===" -f $code)

$resultFile = 'D:\VMs\WarpCordTest\test-result.json'
if (Test-Path $resultFile) {
    $report = Get-Content $resultFile -Raw | ConvertFrom-Json
    Log ("SONUC: {0} ({1}/{2})" -f $report.verdict, $report.passed, $report.total)
}
exit $code
