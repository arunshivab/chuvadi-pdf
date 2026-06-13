# Distributing Chuvadi

## Principle

Chuvadi is a **general-purpose PDF library built for the whole world to use.**
It is not designed, shaped, or packaged around any single consumer. Applications
that use it — including the Chuvadi Reader — are downstream consumers like any
other, and their needs do not drive the library's public surface or its
packaging. Every packaging decision here assumes an anonymous developer
somewhere pulling Chuvadi from NuGet to build something we will never see.

That framing is deliberate: it keeps the modules cohesive, the dependencies
honest (zero external NuGet dependencies in `src/`), and the public API stable
for everyone rather than convenient for one app.

## What ships

Each `src/` module is published as its own NuGet package, so a consumer can take
exactly what they need:

- `Chuvadi.Pdf.Primitives`, `Chuvadi.Pdf.Objects`, `Chuvadi.Pdf.IO`,
  `Chuvadi.Pdf.Documents` — the object model and read/write core.
- `Chuvadi.Pdf.Filters`, `Chuvadi.Pdf.Graphics`, `Chuvadi.Pdf.Color`,
  `Chuvadi.Pdf.Images`, `Chuvadi.Pdf.Fonts`, `Chuvadi.Pdf.Fonts.Rendering`,
  `Chuvadi.Pdf.Fonts.Woff2` — primitives for content and rendering.
- `Chuvadi.Pdf.Content`, `Chuvadi.Pdf.Text`, `Chuvadi.Pdf.Rendering`,
  `Chuvadi.Pdf.Rendering.DisplayList`, `Chuvadi.Pdf.Svg` — content,
  text extraction, rasterization, SVG.
- `Chuvadi.Pdf.Authoring`, `Chuvadi.Pdf.Operations`, `Chuvadi.Pdf.Annotations`,
  `Chuvadi.Pdf.Forms`, `Chuvadi.Pdf.Redaction`, `Chuvadi.Pdf.Watermark`,
  `Chuvadi.Pdf.Signatures`, `Chuvadi.Pdf.Reader`, `Chuvadi.Cryptography`,
  `Chuvadi.Pdf.Encryption` — higher-level operations.
- **`Chuvadi.Pdf`** — a convenience **meta-package**. It has no code of its own;
  installing it pulls in the entire cross-platform stack as dependencies, so a
  consumer who just wants "the PDF library" runs one command:
  `dotnet add package Chuvadi.Pdf`.

`Chuvadi.Pdf.Rendering.Wpf` targets `net10.0-windows` and is a separate,
Windows-only package. It is **not** referenced by the `Chuvadi.Pdf`
meta-package, so the meta-package stays cross-platform.

Not packed: tests, examples, `tools/Chuvadi.Pdf.Cli`, and benchmarks — all marked
`IsPackable=false`.

## Versioning

One version for the whole library (mono-versioning). All packages are stamped
with the same `-p:Version=x.y.z`, which makes the inter-package dependency
versions line up automatically (a `ProjectReference` becomes a package
dependency at the same version). This matches the existing `git tag vX.Y.Z`
release flow.

## Build and publish

From the repository root:

```powershell
# 1. Produce every package at a version (builds + tests first, then packs)
.\build\pack.ps1 -Version 2.8.4
#    -> artifacts\nupkg\*.nupkg

# 2. Push to nuget.org (dry-run first to see what would go)
.\build\publish.ps1 -ApiKey $env:NUGET_API_KEY -DryRun
.\build\publish.ps1 -ApiKey $env:NUGET_API_KEY
```

`pack.ps1` cleans `bin`/`obj`, builds Release, runs the full test suite (pass
`-NoTest` to skip), then packs the solution. `publish.ps1` pushes with
`--skip-duplicate`, so it is safe to re-run after a partial failure — NuGet
versions are immutable once live.

> The meta-package must be in the solution for the solution-level pack to
> include it. One-time:
> `dotnet sln Chuvadi.slnx add src\Chuvadi.Pdf\Chuvadi.Pdf.csproj`

## Pre-publish checklist (before the first public push)

These are decisions and one-time setup items, not code:

1. **Project / repository URL.** `Directory.Build.props` currently sets
   `PackageProjectUrl` and `RepositoryUrl` to `https://github.com/chuvadi/chuvadi`,
   which is a placeholder. Point both at the real **public** home of the project
   before publishing — this URL is shown on nuget.org and drives source linking.
2. **Repository visibility / Source Link.** Source Link (jump-to-source from the
   debugger) only works if the repository is publicly reachable at that URL.
   Decide whether the canonical repo is public; if so, the embedded PDBs plus a
   `Microsoft.SourceLink.GitHub` reference give consumers real source debugging.
3. **Reserve the `Chuvadi` ID prefix** on nuget.org (optional but recommended)
   so the `Chuvadi.*` and `Chuvadi.Pdf.*` package IDs are protected.
4. **Package icon.** `icon.png` at the repo root is currently a tiny placeholder.
   Replace it with the real project icon (128×128 PNG is the usual choice).
5. **Documentation completeness.** `Directory.Build.props` suppresses `CS1591`
   (missing XML doc warnings) during development. Remove that suppression before
   a 1.0 release so every public member is documented in the shipped packages.
6. **API key.** Store the nuget.org API key as `NUGET_API_KEY` (environment
   variable / CI secret); never commit it or pass it where it could be logged.
