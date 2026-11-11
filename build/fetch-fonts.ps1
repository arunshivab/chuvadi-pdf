<#
.SYNOPSIS
    Fetches the Chuvadi font packages into the repository's local font feed.

.DESCRIPTION
    chuvadi-pdf depends on two font packages, published as release assets of
    the Chuvadi monorepo, not on nuget.org (D-46, D-147, D-311):
    - Chuvadi.Fonts 1.1.0 (the LiPi Sans faces);
    - Chuvadi.Fonts.Liberation 1.0.0 (the Liberation faces the standard-14
      embedding engine uses, 3.33.0), which depends on Chuvadi.Fonts 1.1.0.

    This script downloads each file into .fonts-feed\ (gitignored), checks its
    SHA-256 against the value pinned below, and fails if they differ.
    nuget.config maps both package ids to that folder only.

    Run it once before restoring or building. A file that is already present
    and matches its hash is not downloaded again. Works in Windows PowerShell 5
    and in PowerShell 7 (pwsh) on Windows, Linux and macOS.

.EXAMPLE
    .\build\fetch-fonts.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Packages = @(
    @{
        Id = 'Chuvadi.Fonts'
        File = 'Chuvadi.Fonts.1.1.0.nupkg'
        Url = 'https://github.com/arunshivab/chuvadi/releases/download/fonts-v1.1.0/Chuvadi.Fonts.1.1.0.nupkg'
        Sha256 = 'E6F5FFA6061DBAB3D472C100E1E91846B33AF485D2D2CB7A956902834D1F742C'
    },
    @{
        Id = 'Chuvadi.Fonts.Liberation'
        File = 'Chuvadi.Fonts.Liberation.1.0.0.nupkg'
        Url = 'https://github.com/arunshivab/chuvadi/releases/download/fonts-liberation-v1.0.0/Chuvadi.Fonts.Liberation.1.0.0.nupkg'
        Sha256 = '6F4EB33E671DD8D9D5A2C2B4C238F333AC6B1216A8DE3B8AD868A2A1D6540BA2'
    }
)

$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root '.fonts-feed'

function Get-Sha256([string] $path) {
    return (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToUpperInvariant()
}

if (-not (Test-Path $feed)) {
    New-Item -ItemType Directory -Path $feed | Out-Null
}

# Windows PowerShell 5 does not enable TLS 1.2 by default.
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

foreach ($package in $Packages) {
    $id = $package.Id
    $target = Join-Path $feed $package.File

    if ((Test-Path $target) -and ((Get-Sha256 $target) -eq $package.Sha256)) {
        Write-Host "${id}: $($package.File) already present and verified."
        continue
    }

    $partial = "$target.download"
    if (Test-Path $partial) {
        Remove-Item $partial -Force
    }

    Write-Host "${id}: downloading $($package.Url)"
    $previous = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $package.Url -OutFile $partial -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previous
    }

    $actual = Get-Sha256 $partial
    if ($actual -ne $package.Sha256) {
        Remove-Item $partial -Force
        throw "${id}: SHA-256 mismatch. Expected $($package.Sha256), got $actual. The file was not kept."
    }

    Move-Item -Path $partial -Destination $target -Force
    Write-Host "${id}: $($package.File) verified ($($package.Sha256))."
}
