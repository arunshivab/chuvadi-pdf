<#
.SYNOPSIS
    Attaches a released version's packages to its GitHub release.

.DESCRIPTION
    From 3.32.0 (D-234, D-240) every chuvadi-pdf release attaches its .nupkg
    files, with a SHA256SUMS.txt, to a GitHub release on the version's tag, so
    the monorepo release runner can restore them. Run it after the signed tag
    is pushed and after build\pack.ps1 has written the packages (the same
    files that are copied to local-nuget).

    The script checks before it changes anything:
    - the tag vX.Y.Z exists locally and on origin;
    - no GitHub release exists yet for the tag (it never replaces one);
    - the package folder holds exactly one Chuvadi.*.X.Y.Z.nupkg for every
      src project and nothing else at that version.
    It then writes SHA256SUMS.txt (lower-case hash, two spaces, file name;
    LF; UTF-8 without BOM; names in ordinal order, the format sha256sum -c
    reads), creates the release with --verify-tag (so it never makes a new
    tag), and downloads SHA256SUMS.txt again to check the upload.

    The font packages are not attached: Chuvadi.Fonts belongs to the
    monorepo's own fonts-v1.1.0 release and Chuvadi.Fonts.Liberation to its
    fonts-liberation-v1.0.0 release. nuget.org is not touched (that is
    publish.ps1).

    Needs the GitHub CLI (gh), signed in with the repo scope. Works in
    Windows PowerShell 5 and in PowerShell 7.

.PARAMETER Version
    The released version, e.g. 3.32.0. The tag is v<Version>.

.PARAMETER PackageDir
    The folder holding the packages. Default: artifacts\nupkg.

.PARAMETER DryRun
    Run every check and write SHA256SUMS.txt, but do not create the release.

.EXAMPLE
    .\build\release-assets.ps1 -Version 3.32.0 -DryRun

.EXAMPLE
    .\build\release-assets.ps1 -Version 3.32.0
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $PackageDir = "artifacts\nupkg",
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid SemVer (e.g. 3.32.0)."
}

$tag = "v$Version"

# Runs a native command and returns its exit code, without letting its
# stderr stop the script (Windows PowerShell 5 turns redirected native stderr
# into errors under ErrorActionPreference Stop).
function Invoke-Quiet([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $command *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

Write-Host "Chuvadi release assets" -ForegroundColor Cyan
Write-Host "  Tag      : $tag" -ForegroundColor Gray
Write-Host "  Packages : $PackageDir" -ForegroundColor Gray
Write-Host ""

# 1. The GitHub CLI is installed and signed in.
if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "The GitHub CLI (gh) is not installed."
}
if ((Invoke-Quiet { gh auth status }) -ne 0) {
    throw "gh is not signed in. Run: gh auth login"
}

# 2. The tag exists here and on origin.
if ((Invoke-Quiet { git rev-parse -q --verify "refs/tags/$tag" }) -ne 0) {
    throw "Tag $tag does not exist in this clone. Create and push the signed tag first."
}
$remoteTag = git ls-remote --tags origin "refs/tags/$tag"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($remoteTag | Out-String))) {
    throw "Tag $tag is not on origin. Push it first: git push origin $tag"
}

# 3. No release exists for the tag yet.
if ((Invoke-Quiet { gh release view $tag }) -eq 0) {
    throw "A GitHub release for $tag already exists. This script never replaces one; check it by hand."
}

# 4. Exactly one package per src project, and nothing else at this version.
if (-not (Test-Path $PackageDir)) {
    throw "Package folder '$PackageDir' not found. Run build\pack.ps1 -Version $Version first."
}
$projects = @(Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.csproj | ForEach-Object { $_.BaseName })
$expected = @($projects | ForEach-Object { "$_.$Version.nupkg" })
$found = @(Get-ChildItem $PackageDir -Filter "Chuvadi.*.$Version.nupkg" | ForEach-Object { $_.Name })

$missing = @($expected | Where-Object { $found -notcontains $_ })
$extra = @($found | Where-Object { $expected -notcontains $_ })
if ($missing.Count -gt 0) {
    throw "Packages missing from '$PackageDir':`n  $($missing -join "`n  ")"
}
if ($extra.Count -gt 0) {
    throw "Unexpected packages in '$PackageDir' (no src project of that name):`n  $($extra -join "`n  ")"
}

[string[]] $names = $expected
[Array]::Sort($names, [StringComparer]::Ordinal)
$files = @($names | ForEach-Object { (Resolve-Path (Join-Path $PackageDir $_)).Path })
Write-Host "  Found all $($files.Count) packages." -ForegroundColor Gray

# 5. SHA256SUMS.txt in a fresh folder, so the uploaded file is named exactly that.
$work = Join-Path ([System.IO.Path]::GetTempPath()) "chuvadi-release-$Version"
if (Test-Path $work) {
    Remove-Item $work -Recurse -Force
}
New-Item -ItemType Directory -Path $work | Out-Null
$sums = Join-Path $work "SHA256SUMS.txt"
$lines = @($files | ForEach-Object {
    (Get-FileHash -Path $_ -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + [System.IO.Path]::GetFileName($_)
})
[System.IO.File]::WriteAllText($sums, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host ""
Get-Content $sums | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
Write-Host ""

if ($DryRun) {
    Write-Host "Dry run: every check passed; the release was not created." -ForegroundColor Cyan
    Write-Host "SHA256SUMS.txt is at $sums" -ForegroundColor Cyan
    exit 0
}

# 6. Create the release on the existing tag.
$notes = "chuvadi-pdf $Version packages for the monorepo release runner. Verify with SHA256SUMS.txt. Chuvadi.Fonts is in the monorepo's fonts-v1.1.0 release and Chuvadi.Fonts.Liberation in its fonts-liberation-v1.0.0 release."
$createArgs = @('release', 'create', $tag) + $files + @($sums, '--verify-tag', '--title', $tag, '--notes', $notes)
& gh @createArgs
if ($LASTEXITCODE -ne 0) {
    throw "gh release create failed. Check the release page before running again."
}

# 7. Check the upload: the asset count, and SHA256SUMS.txt read back.
$assetCount = [int](gh release view $tag --json assets --jq ".assets | length")
if ($assetCount -ne $files.Count + 1) {
    throw "The release has $assetCount assets; expected $($files.Count + 1)."
}
$check = Join-Path $work "check"
New-Item -ItemType Directory -Path $check | Out-Null
gh release download $tag --pattern "SHA256SUMS.txt" --dir $check
if ($LASTEXITCODE -ne 0) {
    throw "Could not download SHA256SUMS.txt back from the release."
}
if ((Get-FileHash (Join-Path $check "SHA256SUMS.txt")).Hash -ne (Get-FileHash $sums).Hash) {
    throw "SHA256SUMS.txt on the release differs from the one written here."
}

Remove-Item $work -Recurse -Force
Write-Host ""
Write-Host "Release $tag has $assetCount assets ($($files.Count) packages and SHA256SUMS.txt)." -ForegroundColor Green
