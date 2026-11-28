# Hosts the fragmenting proxy inside a signed powershell.exe process, so no
# unsigned exe has to run. Started by warpac.ps1; blocks while serving.
param(
    [int]$Port = 1081,
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'warpcore.ps1')

$source = Join-Path $PSScriptRoot 'warpfrag\WarpFrag.cs'
if (-not (Test-Path $source)) { Write-Error "WarpFrag.cs bulunamadi: $source"; exit 2 }

Add-Type -Path $source -ErrorAction Stop

$proxyArgs = @('--port', "$Port")
if ($LogPath) { $proxyArgs += @('--log', $LogPath) }

# Main blocks in its listener loop until the process is killed.
[WarpFrag]::Main($proxyArgs)
