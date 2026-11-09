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

.PARAMETER BaselineVersion
    The previous release, e.g. 3.31.1 (3.31.2, D-221). With -BaselineDir, the
    SDK package validation compares every package against that release and
    fails the pack on any public API removed or changed since. Both must be
    given together.

.PARAMETER BaselineDir
    The folder holding the -BaselineVersion .nupkg files, normally
    C:\Users\aruns\Documents\local-nuget. Every package being packed must
    have its baseline there, or the script stops before building.

.EXAMPLE
    .\build\pack.ps1 -Version 2.8.4

.EXAMPLE
    .\build\pack.ps1 -Version 3.31.2 -BaselineVersion 3.31.1 -BaselineDir C:\Users\aruns\Documents\local-nuget
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $OutputDir = "artifacts\nupkg",
    [switch] $NoTest,
    [string] $BaselineVersion = "",
    [string] $BaselineDir = ""
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

if (($BaselineVersion -eq "") -ne ($BaselineDir -eq "")) {
    throw "Give -BaselineVersion and -BaselineDir together, or neither."
}

$packArgs = @()
if ($BaselineVersion -ne "") {
    if ($BaselineVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Baseline version '$BaselineVersion' is not a valid SemVer."
    }
    $BaselineDir = (Resolve-Path $BaselineDir).Path

    # Every packable src/ project needs its baseline package; a missing one
    # would otherwise skip that package's comparison without a word.
    $missing = @()
    foreach ($proj in Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.csproj) {
        $baseline = Join-Path $BaselineDir "$($proj.BaseName).$BaselineVersion.nupkg"
        if (-not (Test-Path $baseline)) { $missing += $baseline }
    }
    if ($missing.Count -gt 0) {
        throw "Baseline packages not found:`n  $($missing -join "`n  ")"
    }
    $packArgs = @("-p:ChuvadiBaselineDir=$BaselineDir", "-p:ChuvadiBaselineVersion=$BaselineVersion")
}

Write-Host "Chuvadi pack" -ForegroundColor Cyan
Write-Host "  Version : $Version" -ForegroundColor Gray
Write-Host "  Output  : $OutputDir" -ForegroundColor Gray
if ($BaselineVersion -ne "") {
    Write-Host "  Baseline: $BaselineVersion from $BaselineDir" -ForegroundColor Gray
} else {
    Write-Host "  Baseline: none (each package is validated on its own)" -ForegroundColor Gray
}
Write-Host ""

# Chuvadi.Fonts comes from the monorepo release, checked by SHA-256 (D-147).
& (Join-Path $PSScriptRoot "fetch-fonts.ps1")

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
dotnet pack Chuvadi.slnx -c Release -p:Version=$Version -o $OutputDir @packArgs
if ($LASTEXITCODE -ne 0) { throw "Pack failed." }

# Consumers restore Chuvadi.Fonts from the same feed as the Chuvadi packages,
# so the verified file is copied next to them (D-147).
Copy-Item -Path (Join-Path $root ".fonts-feed\Chuvadi.Fonts.1.0.0.nupkg") -Destination $OutputDir -Force

$packages = Get-ChildItem $OutputDir -Filter *.nupkg
Write-Host ""
Write-Host "Produced $($packages.Count) package(s):" -ForegroundColor Green
$packages | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor White }
Write-Host ""
Write-Host "Next: copy the packages to local-nuget, push the signed tag v$Version, then run .\build\release-assets.ps1 -Version $Version (D-240)." -ForegroundColor Cyan
