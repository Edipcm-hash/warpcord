# Probes the WARP endpoint candidates from the installed config and prints a
# JSON array for the GUI's "Uçlar" panel:
#   [{"address":"162.159.192.9","port":2408,"ok":true,"ms":151}, ...]
# Each candidate is tested with a real request to the Discord API through a
# throw-away sing-box probe, so a result means "this endpoint works here".
param(
    [string]$InstallDir = 'C:\ProgramData\DiscordTunnel',
    [int]$MaxCount = 8,
    [int]$TimeoutSec = 5
)

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'warpcore.ps1')

function Write-JsonArrayEmpty { Write-Output '[]' }

$singBox = Join-Path $InstallDir 'sing-box.exe'
$configPath = Join-Path $InstallDir 'config.json'
if (-not (Test-Path $singBox) -or -not (Test-Path $configPath)) { Write-JsonArrayEmpty; exit 0 }

$account = $null
try { $account = Get-WarpAccountFromConfig -ConfigPath $configPath } catch { }
if (-not $account) { Write-JsonArrayEmpty; exit 0 }

$work = Join-Path $env:TEMP ('warpcord-ep-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

$items = New-Object System.Collections.ArrayList
try {
    foreach ($candidate in (Get-WarpEndpointCandidates -Account $account -MaxCount $MaxCount)) {
        $result = Test-WarpEndpoint -SingBoxPath $singBox -Account $account -Endpoint $candidate `
            -WorkDir $work -TimeoutSec $TimeoutSec
        $ok = 'false'
        if ($result.Ok) { $ok = 'true' }
        $line = '{"address":"' + $result.Address + '","port":' + [int]$result.Port + ',"ok":' + $ok + ',"ms":' + [int]$result.Ms + '}'
        [void]$items.Add($line)
    }
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output ('[' + ($items -join ',') + ']')
