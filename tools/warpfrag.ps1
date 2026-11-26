# ============================================================================
#  warpfrag.ps1  -  build and test the DPI-bypass proxy used in DPI mode.
#
#  Usage:
#    .\tools\warpfrag.ps1 -Build              # compile to .\bin\warp-frag.exe
#    .\tools\warpfrag.ps1 -Test               # build + live test against Discord
#    .\tools\warpfrag.ps1 -Run                # run in the foreground (port 1081)
# ============================================================================
param(
    [switch]$Build,
    [switch]$Test,
    [switch]$Run,
    [string]$Output = '',
    [int]$Port = 1081,
    [string]$Mode = 'tlsrec'
)

$ErrorActionPreference = 'Continue'
$RepoDir = Split-Path -Parent $PSScriptRoot
. (Join-Path $RepoDir 'tools\warpcore.ps1')
Initialize-WarpTls

if (-not $Output) { $Output = Join-Path $RepoDir 'bin\warp-frag.exe' }
$source = Get-WarpFragSource -ScriptRoot $RepoDir
Write-Host ''
Write-Host '  WARP CORD  -  DPI proxy' -ForegroundColor Cyan
Write-Host ''

if (-not ($Build -or $Test -or $Run)) { $Test = $true }

if ($Build -or $Test) {
    $outDir = Split-Path -Parent $Output
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    Initialize-WarpLog -Path (Join-Path $outDir 'warpfrag.log') -Title 'WarpCord warpfrag'
    $ok = Build-WarpFrag -SourcePath $source -OutputPath $Output
    Write-Host ''
    if (-not $ok) {
        Write-Host '  Derleme basarisiz. C# derleyicisi (csc.exe) gerekli.' -ForegroundColor Red
        exit 2
    }
    Write-Host "  Derlendi: $Output" -ForegroundColor Green
}

if ($Test) {
    Write-Host ''
    Write-Host "  Test ediliyor (port $Port, mod $Mode)..." -ForegroundColor DarkGray
    $result = Test-WarpFrag -FragPath $Output -Port $Port -LogPath (Join-Path (Split-Path -Parent $Output) 'frag-test.log')
    if ($result.Ok) {
        Write-Host "  OK - Discord API cevap verdi ($($result.Ms) ms)" -ForegroundColor Green
        exit 0
    }
    $detail = $result.Detail
    if ($detail.Length -gt 160) { $detail = $detail.Substring(0, 160) + '...' }
    Write-Host "  BASARISIZ ($($result.Ms) ms): $detail" -ForegroundColor Red
    exit 2
}

if ($Run) {
    Write-Host "  Calisiyor: http proxy 127.0.0.1:$Port - kapatmak icin Ctrl+C" -ForegroundColor Yellow
    & $Output --port $Port --mode $Mode --delay 3
}
