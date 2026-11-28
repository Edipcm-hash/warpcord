# Prints the host readiness posture as one JSON object, for the GUI's
# "Hazırlık" card. Read-only: no changes, no elevation required.
#   warpreadiness.ps1
param()

$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'warpcore.ps1')

$posture = Get-WarpSecurityPosture
$motw = 0
try { $motw = [int](Get-WarpMotwCount -Path $PSScriptRoot) } catch { }

$row = [ordered]@{
    admin    = [bool]$posture.IsAdmin
    language = [string]$posture.LanguageMode
    policy   = [string]$posture.ExecutionPolicy
    gpo      = [bool]$posture.PolicyEnforcedByGpo
    defender = [bool]$posture.DefenderPresent
    realtime = [bool]$posture.DefenderRealTime
    asr      = @($posture.AsrEnabledIds).Count
    cfa      = [int]$posture.ControlledFolderAccess
    sac      = [string]$posture.SmartAppControl
    wdac     = [bool]$posture.WdacEnforced
    motw     = $motw
}

Write-Output ($row | ConvertTo-Json -Compress)
