# Prints the Smart App Control state as a single number, for sac-check.bat.
#   0 = off, 1 = on, 2 = evaluation, -1 = unknown / not present
# WARPCORD_SAC_FORCE overrides the registry so the .bat branches can be tested.
param()

$ErrorActionPreference = 'Continue'

$forced = $env:WARPCORD_SAC_FORCE
if ($forced) {
    switch ($forced) {
        '0' { Write-Output 0; exit 0 }
        '1' { Write-Output 1; exit 0 }
        '2' { Write-Output 2; exit 0 }
        default { Write-Output (-1); exit 0 }
    }
}

try {
    $policy = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' `
        -Name 'VerifiedAndReputablePolicyState' -ErrorAction Stop
    $state = [int]$policy.VerifiedAndReputablePolicyState
    if ($state -lt 0 -or $state -gt 2) { $state = -1 }
    Write-Output $state
} catch {
    Write-Output (-1)
}
