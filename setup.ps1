#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates the Chuvadi solution file and wires up all projects.
    Run once on your machine after cloning the repository.

.DESCRIPTION
    This script uses the dotnet CLI to create a proper .sln file
    and add all projects to it. Run it from the repository root.

.EXAMPLE
    ./setup.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "Chuvadi — Solution Setup" -ForegroundColor Cyan
Write-Host "========================" -ForegroundColor Cyan

# ── Verify prerequisites ──────────────────────────────────────────────────────

$dotnetVersion = dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found. Install .NET 10 SDK from https://dot.net"
    exit 1
}

Write-Host "Using .NET SDK: $dotnetVersion" -ForegroundColor Green

# ── Create solution ───────────────────────────────────────────────────────────

if (Test-Path "Chuvadi.sln") {
    Write-Host "Solution file already exists — skipping creation." -ForegroundColor Yellow
} else {
    Write-Host "Creating solution..." -ForegroundColor White
    dotnet new sln --name Chuvadi
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to create solution"; exit 1 }
}

# ── Helper: add project to solution ──────────────────────────────────────────

function Add-Project {
    param([string]$Path)
    Write-Host "  Adding $Path" -ForegroundColor Gray
    dotnet sln Chuvadi.sln add $Path --in-root 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to add $Path to solution"
        exit 1
    }
}

# ── Source projects ───────────────────────────────────────────────────────────

Write-Host "`nAdding source projects..." -ForegroundColor White

Add-Project "src/Chuvadi.Pdf.Primitives/Chuvadi.Pdf.Primitives.csproj"
Add-Project "src/Chuvadi.Pdf.Filters/Chuvadi.Pdf.Filters.csproj"
Add-Project "src/Chuvadi.Pdf.Objects/Chuvadi.Pdf.Objects.csproj"
Add-Project "src/Chuvadi.Pdf.IO/Chuvadi.Pdf.IO.csproj"
Add-Project "src/Chuvadi.Pdf.Documents/Chuvadi.Pdf.Documents.csproj"
Add-Project "src/Chuvadi.Pdf.Content/Chuvadi.Pdf.Content.csproj"
Add-Project "src/Chuvadi.Pdf.Fonts/Chuvadi.Pdf.Fonts.csproj"
Add-Project "src/Chuvadi.Pdf.Text/Chuvadi.Pdf.Text.csproj"
Add-Project "src/Chuvadi.Pdf.Operations/Chuvadi.Pdf.Operations.csproj"

# ── Test projects ─────────────────────────────────────────────────────────────

Write-Host "`nAdding test projects..." -ForegroundColor White

Add-Project "tests/Chuvadi.Pdf.Primitives.Tests/Chuvadi.Pdf.Primitives.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Filters.Tests/Chuvadi.Pdf.Filters.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Objects.Tests/Chuvadi.Pdf.Objects.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.IO.Tests/Chuvadi.Pdf.IO.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Documents.Tests/Chuvadi.Pdf.Documents.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Content.Tests/Chuvadi.Pdf.Content.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Fonts.Tests/Chuvadi.Pdf.Fonts.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Text.Tests/Chuvadi.Pdf.Text.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Operations.Tests/Chuvadi.Pdf.Operations.Tests.csproj"
Add-Project "tests/Chuvadi.Pdf.Integration.Tests/Chuvadi.Pdf.Integration.Tests.csproj"

# ── Tools ─────────────────────────────────────────────────────────────────────

Write-Host "`nAdding tools..." -ForegroundColor White
Add-Project "tools/Chuvadi.Pdf.Cli/Chuvadi.Pdf.Cli.csproj"

# ── Restore and build to verify ───────────────────────────────────────────────

Write-Host "`nRestoring packages..." -ForegroundColor White
& (Join-Path $PSScriptRoot "build\fetch-fonts.ps1")
dotnet restore
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed"; exit 1 }

Write-Host "`nBuilding (should succeed with empty projects)..." -ForegroundColor White
dotnet build --no-restore --configuration Debug
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host "`n✓ Setup complete." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  dotnet build          — build everything"
Write-Host "  dotnet test           — run all tests"
Write-Host "  dotnet run --project tools/Chuvadi.Pdf.Cli -- --help"
Write-Host ""
Write-Host "Development starts at:" -ForegroundColor Cyan
Write-Host "  src/Chuvadi.Pdf.Primitives/"
Write-Host ""
