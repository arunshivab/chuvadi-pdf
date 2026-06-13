<#
.SYNOPSIS
    Publishes the packed Chuvadi NuGet packages to a feed (nuget.org by default).

.DESCRIPTION
    Pushes every .nupkg in the package directory to the target source.
    Uses --skip-duplicate so re-running after a partial failure is safe:
    versions already on the feed are left untouched (NuGet versions are
    immutable once published).

    Publish order does not matter to NuGet, but the meta-package (Chuvadi.Pdf)
    and any package depending on others should ideally be pushed after its
    dependencies are live; --skip-duplicate plus retry handles transient races.

.PARAMETER ApiKey
    The nuget.org (or other feed) API key. Treat as a secret; prefer passing
    via an environment variable rather than inline on the command line.

.PARAMETER Source
    The push source. Default: nuget.org.

.PARAMETER PackageDir
    Folder containing the .nupkg files. Default: artifacts\nupkg.

.PARAMETER DryRun
    List what would be pushed without pushing.

.EXAMPLE
    .\build\publish.ps1 -ApiKey $env:NUGET_API_KEY
#>
param(
    [Parameter(Mandatory = $true)] [string] $ApiKey,
    [string] $Source = "https://api.nuget.org/v3/index.json",
    [string] $PackageDir = "artifacts\nupkg",
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $PackageDir)) {
    throw "Package directory '$PackageDir' not found. Run build\pack.ps1 first."
}

$packages = Get-ChildItem $PackageDir -Filter *.nupkg
if ($packages.Count -eq 0) {
    throw "No .nupkg files in '$PackageDir'. Run build\pack.ps1 first."
}

Write-Host "Chuvadi publish" -ForegroundColor Cyan
Write-Host "  Source   : $Source" -ForegroundColor Gray
Write-Host "  Packages : $($packages.Count)" -ForegroundColor Gray
Write-Host ""

foreach ($pkg in $packages) {
    if ($DryRun) {
        Write-Host "  WOULD PUSH  $($pkg.Name)" -ForegroundColor DarkYellow
        continue
    }

    Write-Host "  Pushing $($pkg.Name)..." -ForegroundColor Gray
    dotnet nuget push $pkg.FullName --api-key $ApiKey --source $Source --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $($pkg.Name)." }
}

Write-Host ""
if ($DryRun) {
    Write-Host "Dry run complete — nothing pushed." -ForegroundColor Cyan
} else {
    Write-Host "All packages pushed to $Source." -ForegroundColor Green
}
