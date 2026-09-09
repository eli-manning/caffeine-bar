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
    [switch]$SingleFile
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK 8.0 or newer is required: https://dotnet.microsoft.com/download'
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
