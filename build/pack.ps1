<#
.SYNOPSIS
    Builds and packs every public Chuvadi NuGet package at a given version.

.DESCRIPTION
    Chuvadi is a general-purpose PDF library published for public consumption.
    This script produces the full set of NuGet packages (one per module, plus
    the Chuvadi.Pdf meta-package) into an output folder, ready to publish.

    Only packable projects are packed: the src/ libraries. Tests, examples,
    tools, and benchmarks are marked IsPackable=false and are skipped.
    Chuvadi.Pdf.Rendering.Wpf targets net10.0-windows and only packs on Windows.

.PARAMETER Version
    The package version, e.g. 2.8.4. Applied uniformly to every package
    (mono-versioning), so inter-package dependencies line up automatically.

.PARAMETER OutputDir
    Where the .nupkg files are written. Default: artifacts\nupkg.

.PARAMETER NoTest
    Skip the test run. By default the full test suite must pass before packing.

.EXAMPLE
    .\build\pack.ps1 -Version 2.8.4
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $OutputDir = "artifacts\nupkg",
    [switch] $NoTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $root "Chuvadi.slnx"))) {
    throw "Chuvadi.slnx not found. Run from the repository (script lives in build\)."
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid SemVer (e.g. 2.8.4 or 2.9.0-rc1)."
}

Write-Host "Chuvadi pack" -ForegroundColor Cyan
Write-Host "  Version : $Version" -ForegroundColor Gray
Write-Host "  Output  : $OutputDir" -ForegroundColor Gray
Write-Host ""

# Clean build artifacts so nothing stale leaks into the packages.
Write-Host "Cleaning bin/obj..." -ForegroundColor Gray
Get-ChildItem -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force

# Clean the output directory.
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Building (Release)..." -ForegroundColor Gray
dotnet build Chuvadi.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $NoTest) {
    Write-Host "Testing..." -ForegroundColor Gray
    dotnet test Chuvadi.slnx -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Not packing a red build." }
}

Write-Host "Packing..." -ForegroundColor Gray
dotnet pack Chuvadi.slnx -c Release -p:Version=$Version -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "Pack failed." }

$packages = Get-ChildItem $OutputDir -Filter *.nupkg
Write-Host ""
Write-Host "Produced $($packages.Count) package(s):" -ForegroundColor Green
$packages | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor White }
Write-Host ""
Write-Host "Next: .\build\publish.ps1 -ApiKey <nuget-api-key>" -ForegroundColor Cyan
