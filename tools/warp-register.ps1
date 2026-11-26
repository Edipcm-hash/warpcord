# ============================================================================
#  warp-register.ps1  -  register a free Cloudflare WARP device and write a
#  ready-to-run sing-box config. Pure PowerShell, no Python required.
#
#  Usage:
#    .\tools\warp-register.ps1                 # register + probe + config.json
#    .\tools\warp-register.ps1 -Dpi            # write the DPI-mode config only
#    .\tools\warp-register.ps1 -NoProbe        # skip endpoint probing
#    .\tools\warp-register.ps1 -Out C:\x\config.json
#
#  The config contains a PRIVATE KEY. It is gitignored; never commit it.
# ============================================================================
param(
    [string]$Out = '',
    [string]$InstallDir = 'C:\ProgramData\DiscordTunnel',
    [string]$SingBox = '',
    [switch]$NoProbe,
    [string]$Endpoint = '',
    [switch]$Dpi,
    [switch]$Force,
    [int]$FragPort = 1081
)

$ErrorActionPreference = 'Continue'
$RepoDir = Split-Path -Parent $PSScriptRoot
. (Join-Path $RepoDir 'tools\warpcore.ps1')

Initialize-WarpTls
if (-not $Out) { $Out = Join-Path $RepoDir 'config.json' }
Initialize-WarpLog -Path (Join-Path $InstallDir 'register.log') -Title 'WarpCord register'

Write-Host ''
Write-Host '  WARP CORD  -  cihaz kaydi' -ForegroundColor Cyan
Write-Host ''

$config = $null

if ($Dpi) {
    $config = New-WarpDpiConfig -LogPath (Join-Path $InstallDir 'tunnel.log') -FragPort $FragPort
    Write-WarpLog 'DPI modu config uretildi' 'OK'
} else {
    $account = $null
    $existing = Join-Path $RepoDir 'config.json'
    if (-not $Force -and (Test-Path $existing)) {
        $account = Get-WarpAccountFromConfig -ConfigPath $existing
        if ($account) { Write-WarpLog 'mevcut config.json icindeki cihaz kullaniliyor' 'OK' }
    }
    if (-not $account) {
        Write-WarpLog 'ucretsiz WARP cihazi kaydediliyor (hesap gerekmez)...' 'STEP'
        $account = New-WarpRegistration
    }
    if (-not $account) {
        Write-WarpLog 'kayit basarisiz. Superonline gibi WARP engelli hatlarda -Dpi kullanin.' 'ERROR'
        exit 2
    }
    $accountJson = Join-Path $InstallDir 'account.json'
    Write-WarpJsonFile $accountJson $account
    Set-WarpPrivateAcl $accountJson

    $selected = $null
    if ($Endpoint) {
        $parts = $Endpoint.Split(':')
        $selected = [pscustomobject]@{ Address = $parts[0]; Port = [int]$parts[1]; Label = 'manuel' }
    } elseif (-not $NoProbe) {
        $singBoxPath = $SingBox
        if (-not $singBoxPath) {
            foreach ($candidate in @(
                (Join-Path $InstallDir 'sing-box.exe'),
                (Join-Path $RepoDir 'bin\sing-box.exe'),
                (Join-Path $RepoDir 'sing-box.exe')
            )) {
                if (Test-Path $candidate) { $singBoxPath = $candidate; break }
            }
        }
        if ($singBoxPath -and (Test-Path $singBoxPath)) {
            New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
            $selected = Select-WarpEndpoint -SingBoxPath $singBoxPath -Account $account -WorkDir $InstallDir -BudgetSeconds 70
        } else {
            Write-WarpLog 'sing-box bulunamadi, uclari denemeden varsayilan uc yazilacak' 'WARN'
        }
    }
    if (-not $selected) {
        $selected = [pscustomobject]@{ Address = $account.EndpointV4; Port = 2408; Label = 'varsayilan' }
    }
    Write-WarpLog "uc: $($selected.Address):$($selected.Port)" 'OK'
    $config = New-WarpTunnelConfig -Account $account -Endpoint $selected -LogPath (Join-Path $InstallDir 'tunnel.log')
}

Write-WarpJsonFile $Out $config
Set-WarpPrivateAcl $Out
Write-Host ''
Write-WarpLog "config yazildi: $Out" 'OK'
Write-WarpLog 'Simdi WarpCord.bat calistirin.' 'INFO'
exit 0
