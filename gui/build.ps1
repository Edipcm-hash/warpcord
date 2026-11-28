<#
.SYNOPSIS
    Builds the WarpCord desktop GUI: one self-contained WarpCord.exe.

.DESCRIPTION
    The GUI is WPF on .NET Framework 4.8, which ships with Windows 10/11, so the
    build only needs the C# compiler that is already in %WINDIR%. No .NET SDK, no
    NuGet, no MSBuild: XAML is embedded as a resource and parsed at run time, and
    the logo is embedded both as the exe icon and as PNG resources.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File gui\build.ps1
    powershell -ExecutionPolicy Bypass -File gui\build.ps1 -Shortcut -Smoke
#>
[CmdletBinding()]
param(
    [switch]$Shortcut,
    [switch]$Smoke,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$gui    = $PSScriptRoot
$root   = Split-Path -Parent $gui
$src    = Join-Path $gui 'src'
$assets = Join-Path $gui 'assets'
$dist   = Join-Path $root 'dist'
$exe    = Join-Path $dist 'WarpCord.exe'

if ($Clean -and (Test-Path $dist)) {
    Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "C# derleyicisi bulunamadi: $csc . Windows 10/11 uzerinde .NET Framework 4.x ile birlikte gelir."
}

function Get-AssemblyPath {
    param([string]$Name)
    $roots = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_64'),
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'),
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_32')
    )
    foreach ($rootDir in $roots) {
        $candidate = Join-Path $rootDir $Name
        if (-not (Test-Path $candidate)) { continue }
        $dll = Get-ChildItem -LiteralPath $candidate -Recurse -Filter "$Name.dll" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($dll) { return $dll.FullName }
    }
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $local = Join-Path $framework "$Name.dll"
    if (Test-Path $local) { return $local }
    throw "Assembly bulunamadi: $Name"
}

$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Get-AssemblyPath 'PresentationFramework'),
    (Get-AssemblyPath 'PresentationCore'),
    (Get-AssemblyPath 'WindowsBase'),
    (Get-AssemblyPath 'System.Xaml'),
    (Get-AssemblyPath 'System.Web.Extensions'),
    (Get-AssemblyPath 'System.Net.Http')
) | ForEach-Object { "/r:$_" }

$resources = @(
    "/resource:$(Join-Path $assets 'warpcord-256.png'),WarpCord.Assets.warpcord-256.png",
    "/resource:$(Join-Path $assets 'warpcord-64.png'),WarpCord.Assets.warpcord-64.png",
    "/resource:$(Join-Path $assets 'warpcord-32.png'),WarpCord.Assets.warpcord-32.png",
    "/resource:$(Join-Path $src 'Theme.xaml'),WarpCord.Theme.xaml",
    "/resource:$(Join-Path $src 'MainWindow.xaml'),WarpCord.MainWindow.xaml",
    "/resource:$(Join-Path $src 'Prompt.xaml'),WarpCord.Prompt.xaml"
)

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "Kaynak dosya yok: $src" }

New-Item -ItemType Directory -Path $dist -Force | Out-Null

Write-Host "Derleniyor: $($sources.Count) dosya -> $exe"
& $csc /nologo /noconfig /target:winexe /platform:anycpu /optimize+ /warn:2 `
    "/out:$exe" `
    "/win32icon:$(Join-Path $assets 'warpcord.ico')" `
    "/win32manifest:$(Join-Path $gui 'app.manifest')" `
    @references @resources @sources

if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz (csc kodu $LASTEXITCODE)" }
if (-not (Test-Path $exe)) { throw "Derleme cikti uretmedi: $exe" }

$size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1KB)
Write-Host "Hazir: $exe ($size KB)"

Copy-Item -LiteralPath (Join-Path $assets 'warpcord.ico') -Destination (Join-Path $dist 'warpcord.ico') -Force
Copy-Item -LiteralPath (Join-Path $assets 'warpcord-256.png') -Destination (Join-Path $dist 'warpcord.png') -Force

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $linkPath = Join-Path $desktop 'WarpCord.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $root
    $link.IconLocation = "$exe,0"
    $link.Description = 'WarpCord - yalnizca Discord trafigi tunelden gecer'
    $link.Save()
    Write-Host "Masaustu kisayolu: $linkPath"
}

if ($Smoke) {
    Write-Host ''
    # a GUI exe is not waited for by "&"; Start-Process -Wait gives us the exit code
    $smokeRun = Start-Process -FilePath $exe -ArgumentList '--smoke' -NoNewWindow -Wait -PassThru
    Write-Host "duman testi cikis kodu: $($smokeRun.ExitCode)"
    if ($smokeRun.ExitCode -ne 0) { exit $smokeRun.ExitCode }
}
