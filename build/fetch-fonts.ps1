<#
.SYNOPSIS
    Fetches the Chuvadi.Fonts package into the repository's local font feed.

.DESCRIPTION
    chuvadi-pdf depends on Chuvadi.Fonts, which is published as a release asset
    of the Chuvadi monorepo, not on nuget.org (D-46, D-147). This script
    downloads that one file into .fonts-feed\ (gitignored), checks its SHA-256
    against the value pinned below, and fails if they differ. nuget.config maps
    the package id Chuvadi.Fonts to that folder only.

    Run it once before restoring or building. A file that is already present
    and matches the hash is not downloaded again. Works in Windows PowerShell 5
    and in PowerShell 7 (pwsh) on Windows, Linux and macOS.

.EXAMPLE
    .\build\fetch-fonts.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackageFile = 'Chuvadi.Fonts.1.0.0.nupkg'
$Url = 'https://github.com/arunshivab/chuvadi/releases/download/fonts-v1.0.0/Chuvadi.Fonts.1.0.0.nupkg'
$Sha256 = 'C15A82D1CAA3D70F3E2ABE16DD94201EA1D3DAE91764735A19FFA7398C37F5FE'

$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root '.fonts-feed'
$target = Join-Path $feed $PackageFile

function Get-Sha256([string] $path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToUpperInvariant()
}

if (-not (Test-Path $feed)) {
    New-Item -ItemType Directory -Path $feed | Out-Null
}

if ((Test-Path $target) -and ((Get-Sha256 $target) -eq $Sha256)) {
    Write-Host "Chuvadi.Fonts: $PackageFile already present and verified."
    exit 0
}

# Windows PowerShell 5 does not enable TLS 1.2 by default.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

$partial = "$target.download"
if (Test-Path $partial) {
    Remove-Item $partial -Force
}

Write-Host "Chuvadi.Fonts: downloading $Url"
$previous = $ProgressPreference
$ProgressPreference = 'SilentlyContinue'
try {
    Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
}
finally {
    $ProgressPreference = $previous
}

$actual = Get-Sha256 $partial
if ($actual -ne $Sha256) {
    Remove-Item $partial -Force
    throw "Chuvadi.Fonts: SHA-256 mismatch. Expected $Sha256, got $actual. The file was not kept."
}

Move-Item -Path $partial -Destination $target -Force
Write-Host "Chuvadi.Fonts: $PackageFile verified ($Sha256)."
