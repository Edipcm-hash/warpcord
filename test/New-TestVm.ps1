# ============================================================================
#  WarpCord test VM (Hyper-V host side)
#
#  Builds a Gen2 Windows 11 VM with virtual TPM and Secure Boot, installs it
#  unattended from an official ISO, then (optionally) copies the repo in and
#  runs test/Test-CleanWindows.ps1 through PowerShell Direct.
#
#  Requires:
#    * Windows 11 Pro/Enterprise with the Hyper-V role enabled
#      (Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All
#       then reboot)
#    * an official Windows 11 ISO, e.g. the Enterprise evaluation build:
#      https://www.microsoft.com/en-us/evalcenter/download-windows-11-enterprise
#    * 8 GB free RAM and ~90 GB free disk for the VM
#
#  Usage:
#    .\New-TestVm.ps1 -CheckOnly
#    .\New-TestVm.ps1 -IsoPath D:\iso\Win11.iso
#    .\New-TestVm.ps1 -IsoPath D:\iso\Win11.iso -GuestUser Test -GuestPass 'Test1234!'
#    .\New-TestVm.ps1 -Destroy
# ============================================================================
param(
    [string]$IsoPath,
    [string]$VmName = 'WarpCordTest',
    [string]$VmRoot = 'D:\VMs',
    [int]$MemoryGB = 6,
    [int]$CpuCount = 4,
    [int]$DiskGB = 80,
    [string]$SwitchName = 'Default Switch',
    [string]$GuestUser = '',
    [string]$GuestPass = '',
    [switch]$SecureBoot,
    [switch]$CheckOnly,
    [switch]$Destroy
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Fail([string]$Message) {
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    exit 1
}
function Info([string]$Message) { Write-Host "  [ -- ] $Message" -ForegroundColor Gray }
function Ok([string]$Message) { Write-Host "  [ OK ] $Message" -ForegroundColor Green }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail 'Yonetici PowerShell gerekli.'
}

$hyperV = [bool](Get-Command Get-VM -ErrorAction SilentlyContinue)
Info ("Hyper-V: " + $(if ($hyperV) { 'hazir' } else { 'yok' }))
if (-not $hyperV) {
    Write-Host ''
    Write-Host '  Hyper-V rolu kurulu degil. Acmak icin (yeniden baslatma gerekir):' -ForegroundColor Yellow
    Write-Host '    Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -All' -ForegroundColor White
    Write-Host '    Restart-Computer' -ForegroundColor White
    if ($CheckOnly) { exit 2 }
    Fail 'Hyper-V olmadan VM olusturulamaz.'
}
$vmms = Get-Service vmms -ErrorAction SilentlyContinue
if (-not $vmms) { Fail 'vmms servisi yok: Hyper-V platformu eksik.' }
Info ("vmms: " + $vmms.Status)

if ($Destroy) {
    $vm = Get-VM -Name $VmName -ErrorAction SilentlyContinue
    if ($vm) {
        if ($vm.State -ne 'Off') { Stop-VM -Name $VmName -TurnOff -Force -ErrorAction SilentlyContinue }
        Remove-VM -Name $VmName -Force
        Ok "VM silindi: $VmName"
    } else { Info "VM yok: $VmName" }
    $vmDir = Join-Path $VmRoot $VmName
    if (Test-Path $vmDir) { Remove-Item $vmDir -Recurse -Force; Ok "Klasor silindi: $vmDir" }
    exit 0
}

if (-not $IsoPath -or -not (Test-Path $IsoPath)) { Fail 'Windows 11 ISO bulunamadi (-IsoPath).' }
$isoItem = Get-Item $IsoPath
if ($isoItem.Length -lt 3GB) { Fail "ISO cok kucuk ($([math]::Round($isoItem.Length/1GB,2)) GB): indirme tamamlanmamis olabilir." }
Ok ("ISO: {0} ({1} GB)" -f $isoItem.Name, [math]::Round($isoItem.Length / 1GB, 2))

$disk = Get-PSDrive -Name 'D' -ErrorAction SilentlyContinue
if ($disk -and $disk.Free -lt ($DiskGB + 10) * 1GB) { Fail "D: uzerinde yeterli yer yok ($([math]::Round($disk.Free/1GB,1)) GB)." }

if ($CheckOnly) { Ok 'Ortam hazir gorunuyor.'; exit 0 }

# --------------------------------------------------------------- answer VHD --
# IMAPI2 is not scriptable on every Windows build (the image stream refuses
# SaveToFile), so the answer file travels on a tiny FAT32 VHDX instead. Windows
# Setup scans every attached volume for \autounattend.xml during windowsPE.
$vmDir = Join-Path $VmRoot $VmName
$vhdPath = Join-Path $vmDir "$VmName.vhdx"
$answerVhd = Join-Path $vmDir 'answer.vhdx'
New-Item -ItemType Directory -Force -Path $vmDir | Out-Null

Info 'Cevap dosyasi VHD olarak hazirlaniyor...'
try {
    if (Test-Path $answerVhd) { Dismount-VHD -Path $answerVhd -ErrorAction SilentlyContinue; Remove-Item $answerVhd -Force }
    New-VHD -Path $answerVhd -SizeBytes 64MB -Dynamic | Out-Null
    $mounted = Mount-VHD -Path $answerVhd -Passthru
    $answerDisk = $mounted | Get-Disk
    Initialize-Disk -Number $answerDisk.Number -PartitionStyle MBR -Confirm:$false -ErrorAction SilentlyContinue
    $answerPart = New-Partition -DiskNumber $answerDisk.Number -UseMaximumSize -AssignDriveLetter
    Format-Volume -Partition $answerPart -FileSystem FAT32 -NewFileSystemLabel 'AUTOUNATTEND' -Confirm:$false -Force | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'autounattend.xml') ("{0}:\autounattend.xml" -f $answerPart.DriveLetter) -Force
    Dismount-VHD -Path $answerVhd
} catch {
    try { Dismount-VHD -Path $answerVhd -ErrorAction SilentlyContinue } catch { }
    Fail ("Cevap VHD'si olusturulamadi: {0}" -f $_.Exception.Message)
}
Ok 'Cevap VHD hazir'

# --------------------------------------------------------------------- VM ----
$existing = Get-VM -Name $VmName -ErrorAction SilentlyContinue
if ($existing) { Fail "VM zaten var: $VmName. Once -Destroy ile silin." }

Info "VM olusturuluyor: $VmName ($MemoryGB GB, $CpuCount vCPU, $DiskGB GB)"
New-Item -ItemType Directory -Force -Path $vmDir | Out-Null
New-VHD -Path $vhdPath -SizeBytes ($DiskGB * 1GB) -Dynamic | Out-Null
New-VM -Name $VmName -Generation 2 -MemoryStartupBytes ($MemoryGB * 1GB) -VHDPath $vhdPath -Path $vmDir -SwitchName $SwitchName | Out-Null
Set-VMProcessor -VMName $VmName -Count $CpuCount
Set-VMMemory -VMName $VmName -DynamicMemoryEnabled $true -MinimumBytes 2GB -MaximumBytes ($MemoryGB * 1GB)
if ($SecureBoot) {
    Set-VMFirmware -VMName $VmName -EnableSecureBoot On -SecureBootTemplate 'MicrosoftWindows'
} else {
    # Windows 11 24H2 media is signed with the Windows UEFI CA 2023, which the
    # host's 2011-era Secure Boot templates do not trust ('boot loader failed').
    # The guest still gets a clean Windows; the OS itself is not modified.
    Set-VMFirmware -VMName $VmName -EnableSecureBoot Off
}
Set-VMKeyProtector -VMName $VmName -NewLocalKeyProtector
Enable-VMTPM -VMName $VmName
$dvd = Add-VMDvdDrive -VMName $VmName -Path $IsoPath -Passthru
Add-VMHardDiskDrive -VMName $VmName -Path $answerVhd -ControllerType SCSI | Out-Null
Set-VMFirmware -VMName $VmName -FirstBootDevice $dvd
Ok 'VM hazir (vTPM + Secure Boot acik)'

Info 'VM baslatiliyor; kurulum ~15-25 dakika surer...'
Start-VM -Name $VmName

# ------------------------------------------------------------- wait/verify ---
$deadline = (Get-Date).AddMinutes(45)
while ((Get-Date) -lt $deadline) {
    $hb = Get-VMIntegrationService -VMName $VmName -Name 'Heartbeat' -ErrorAction SilentlyContinue
    if ($hb -and $hb.PrimaryStatusDescription -eq 'OK') { break }
    Start-Sleep -Seconds 20
}
if (-not $hb -or $hb.PrimaryStatusDescription -ne 'OK') { Fail 'VM 45 dakika icinde heartbeat vermedi (kurulum takildi olabilir).' }
Ok 'VM ayakta (heartbeat OK)'

if (-not $GuestUser -or -not $GuestPass) {
    Write-Host ''
    Write-Host '  Sirada (VM icinde):' -ForegroundColor Cyan
    Write-Host '    1) Bu repoyu VM icine kopyalayin (RDP/panoya kopyala-yapistir).' -ForegroundColor White
    Write-Host '    2) Yonetici PowerShell acip:' -ForegroundColor White
    Write-Host '       .\test\Test-CleanWindows.ps1' -ForegroundColor White
    Write-Host ''
    Write-Host '  Ya da bu betigi -GuestUser/-GuestPass ile tekrar calistirin; PowerShell Direct ile otomatik yapar.' -ForegroundColor DarkGray
    exit 0
}

# ---------------------------------------------------- PowerShell Direct run --
$cred = New-Object System.Management.Automation.PSCredential($GuestUser, (ConvertTo-SecureString $GuestPass -AsPlainText -Force))
$deadline = (Get-Date).AddMinutes(10)
$session = $null
while ((Get-Date) -lt $deadline) {
    try { $session = New-PSSession -VMName $VmName -Credential $cred -ErrorAction Stop; break } catch { Start-Sleep -Seconds 10 }
}
if (-not $session) { Fail 'PowerShell Direct baglantisi kurulamadi (hesap/parola veya ag hazir degil).' }
Ok 'PowerShell Direct baglandi'

Info 'Repo VM icine kopyalaniyor...'
Invoke-Command -Session $session -ScriptBlock {
    param($target)
    if (Test-Path $target) { Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
} -ArgumentList 'C:\Users\Test\Desktop\WarpCord'
Copy-Item -ToSession $session -Path $repo -Destination 'C:\Users\Test\Desktop' -Recurse -Force

Info 'Test calisiyor (VM icinde)...'
$result = Invoke-Command -Session $session -ScriptBlock {
    & 'C:\Users\Test\Desktop\WarpCord\test\Test-CleanWindows.ps1' -Mode auto
    if (Test-Path 'C:\ProgramData\WarpCord-test-result.json') {
        Get-Content 'C:\ProgramData\WarpCord-test-result.json' -Raw
    }
}
$json = ($result | Where-Object { $_ -match '"verdict"' } | Select-Object -Last 1)
if ($json) {
    $report = $json | ConvertFrom-Json
    Write-Host ''
    Write-Host ("  SONUC: {0} ({1}/{2})" -f $report.verdict, $report.passed, $report.total) -ForegroundColor $(if ($report.verdict -eq 'PASS') { 'Green' } else { 'Red' })
    $local = Join-Path $vmDir 'test-result.json'
    Set-Content -Path $local -Value $json -Encoding UTF8
    Ok "Rapor: $local"
    if ($report.verdict -eq 'PASS') { exit 0 }
} else {
    Fail 'Test sonucu alinamadi.'
}
exit 1
