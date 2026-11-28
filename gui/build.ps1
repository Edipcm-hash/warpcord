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
    [switch]$Clean,
    [string]$Certificate = '',
    [string]$PfxPassword = '',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
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
    (Join-Path $frameworkDir 'System.Drawing.dll'),
    (Join-Path $frameworkDir 'System.Windows.Forms.dll'),
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
    "/resource:$(Join-Path $src 'Prompt.xaml'),WarpCord.Prompt.xaml",
    "/resource:$(Join-Path $src 'SettingsWindow.xaml'),WarpCord.SettingsWindow.xaml"
)

# --- embedded engine ---------------------------------------------------------
# The PowerShell engine travels inside the exe, so a lone WarpCord.exe copied to
# a Desktop still installs/repairs. It is unpacked to
# %LOCALAPPDATA%\WarpCord\engine\<version>\ on first use (see gui/src/Engine.cs).
$engineEmbed = @(
    @{ File = 'app\install.ps1';             Name = 'WarpCord.Engine.app.install.ps1' },
    @{ File = 'app\status.ps1';              Name = 'WarpCord.Engine.app.status.ps1' },
    @{ File = 'app\uninstall.ps1';           Name = 'WarpCord.Engine.app.uninstall.ps1' },
    @{ File = 'tools\warpcore.ps1';          Name = 'WarpCord.Engine.tools.warpcore.ps1' },
    @{ File = 'tools\warpheal.ps1';          Name = 'WarpCord.Engine.tools.warpheal.ps1' },
    @{ File = 'tools\tani.ps1';              Name = 'WarpCord.Engine.tools.tani.ps1' },
    @{ File = 'tools\warpfrag.ps1';          Name = 'WarpCord.Engine.tools.warpfrag.ps1' },
    @{ File = 'tools\warpfrag\WarpFrag.cs';  Name = 'WarpCord.Engine.tools.warpfrag.WarpFrag.cs' },
    @{ File = 'tools\warpreadiness.ps1';     Name = 'WarpCord.Engine.tools.warpreadiness.ps1' },
    @{ File = 'tools\warpendpoints.ps1';     Name = 'WarpCord.Engine.tools.warpendpoints.ps1' },
    @{ File = 'tools\warpac.ps1';            Name = 'WarpCord.Engine.tools.warpac.ps1' },
    @{ File = 'tools\warphost.ps1';          Name = 'WarpCord.Engine.tools.warphost.ps1' }
)
foreach ($entry in $engineEmbed) {
    $full = Join-Path $root $entry.File
    if (-not (Test-Path -LiteralPath $full)) { throw "Gomulu motor dosyasi yok: $full" }
    $resources += "/resource:$full,$($entry.Name)"
}

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

# --- optional Authenticode signing -------------------------------------------
# Smart App Control (and SmartScreen) block unsigned binaries with no
# reputation, and SAC has no per-app allowlist, so shipping to many machines
# means signing. A CA-issued OV/EV certificate is required (self-signed is not
# trusted); SAC does not accept ECC certificates, use RSA.
if ($Certificate) {
    $signTool = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $signTool) {
        $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        if (Test-Path $kits) {
            $signTool = Get-ChildItem -LiteralPath $kits -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
        }
    }
    if (-not $signTool) {
        throw "signtool.exe bulunamadi. Visual Studio / Windows SDK kurun ya da 'signtool' PATH'e ekleyin."
    }

    $signArgs = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl)
    if ($Certificate -like '*.pfx') {
        $signArgs += @('/f', $Certificate)
        if ($PfxPassword) { $signArgs += @('/p', $PfxPassword) }
    } else {
        $signArgs += @('/sha1', $Certificate)
    }
    $signArgs += $exe

    & $signTool.FullName @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Imzalama basarisiz (signtool kodu $LASTEXITCODE)" }

    $sig = Get-AuthenticodeSignature -LiteralPath $exe
    Write-Host "Imzalandi: $($sig.SignerCertificate.Subject) [$($sig.Status)]"
}

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
