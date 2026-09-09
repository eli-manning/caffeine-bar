<#
.SYNOPSIS
Builds Caffeine Bar for Windows.

.DESCRIPTION
Produces a self-contained, single-file CaffeineBar.exe that runs without a
separate .NET install. Pass -Install to copy the result into
%LOCALAPPDATA%\Programs\Caffeine Bar and start it.

.PARAMETER Runtime
Target architecture: win-x64 (default) or win-arm64.

.PARAMETER Install
Install to %LOCALAPPDATA% and launch after building.

.PARAMETER Installer
Build Caffeine-Bar-Setup.exe instead of a plain folder. Needs Inno Setup 6.3+
(`winget install JRSoftware.InnoSetup`). Publishes both architectures and packs
them into one installer, the same way the release workflow does.

.PARAMETER SingleFile
Bundle everything into one .exe. Convenient to hand to someone, but a
single-file self-contained bundle is a well-known false-positive magnet for
antivirus heuristics, which see a large opaque blob that unpacks itself at
startup. The default multi-file output trips scanners far less often.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$Install,
    [switch]$SingleFile,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK 8.0 or newer is required: https://dotnet.microsoft.com/download'
}

if ($Installer) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $fallback = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $fallback) { $iscc = $fallback }
        else { throw 'Inno Setup 6.3+ is required: winget install JRSoftware.InnoSetup' }
    } else {
        $iscc = $iscc.Source
    }

    # One installer carries both architectures, so both have to be published
    # regardless of which machine is doing the building.
    foreach ($rid in 'win-x64', 'win-arm64') {
        dotnet publish CaffeineBar.Windows.csproj `
            --configuration Release `
            --runtime $rid `
            --self-contained true `
            -p:EnableWindowsTargeting=true `
            --output (Join-Path $PSScriptRoot "installer\payload\$rid")
    }

    $version = ([xml](Get-Content (Join-Path $PSScriptRoot 'CaffeineBar.Windows.csproj'))
        ).Project.PropertyGroup.Version | Where-Object { $_ }
    & $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\CaffeineBar.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

    $setup = Join-Path $PSScriptRoot 'installer\dist\Caffeine-Bar-Setup.exe'
    if (-not (Test-Path $setup)) { throw "Build did not produce $setup" }
    Write-Host "Built $setup"

    if ($Install) { Start-Process $setup }
    return
}

$output = Join-Path $PSScriptRoot "bin\publish\$Runtime"

dotnet publish CaffeineBar.Windows.csproj `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=$($SingleFile.IsPresent.ToString().ToLower()) `
    -p:EnableWindowsTargeting=true `
    --output $output

$exe = Join-Path $output 'CaffeineBar.exe'
if (-not (Test-Path $exe)) { throw "Build did not produce $exe" }
Write-Host "Built $exe"

if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Caffeine Bar'

    # A running copy holds a lock on its own executable.
    Get-Process -Name CaffeineBar -ErrorAction SilentlyContinue | ForEach-Object {
        $_.Kill()
        $_.WaitForExit(5000) | Out-Null
    }

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $output '*') -Destination $target -Recurse -Force

    $installed = Join-Path $target 'CaffeineBar.exe'
    Write-Host "Installed $installed"
    Start-Process $installed
}
