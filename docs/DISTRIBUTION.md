# Distributing Chuvadi

## Principle

Chuvadi is a **general-purpose PDF library built for the whole world to use.**
It is not designed, shaped, or packaged around any single consumer. Applications
that use it — including the Chuvadi Reader — are downstream consumers like any
other, and their needs do not drive the library's public surface or its
packaging. Every packaging decision here assumes an anonymous developer
somewhere pulling Chuvadi to build something we will never see.

That framing is deliberate: it keeps the modules cohesive, the dependencies
honest (zero external NuGet dependencies in `src/`), and the public API stable
for everyone rather than convenient for one app.

## What ships

The library is **33 packable `src/` projects** — 32 modules plus the
`Chuvadi.Pdf` meta-package. Each module is published as its own package, so a
consumer can take exactly what they need:

- **Object model and read/write core** — `Chuvadi.Pdf.Primitives`,
  `Chuvadi.Pdf.Objects`, `Chuvadi.Pdf.IO`, `Chuvadi.Pdf.Documents`,
  `Chuvadi.Pdf.Encryption`, `Chuvadi.Cryptography`.
- **Content, fonts, and colour primitives** — `Chuvadi.Pdf.Filters`,
  `Chuvadi.Pdf.Content`, `Chuvadi.Pdf.Graphics`, `Chuvadi.Pdf.Color`,
  `Chuvadi.Pdf.Images`, `Chuvadi.Pdf.Fonts`, `Chuvadi.Pdf.Fonts.Rendering`,
  `Chuvadi.Pdf.Fonts.Woff2`.
- **Text and rendering** — `Chuvadi.Pdf.Text`, `Chuvadi.Pdf.Text.Shaping`,
  `Chuvadi.Pdf.Rendering.Walking`, `Chuvadi.Pdf.Rendering.DisplayList`,
  `Chuvadi.Pdf.Rendering.Raster`, `Chuvadi.Pdf.Rendering`, `Chuvadi.Pdf.Svg`.
- **Higher-level operations** — `Chuvadi.Pdf.Authoring`, `Chuvadi.Pdf.Operations`,
  `Chuvadi.Pdf.Annotations`, `Chuvadi.Pdf.Forms`, `Chuvadi.Pdf.Xfa`,
  `Chuvadi.Pdf.Redaction`, `Chuvadi.Pdf.Watermark`, `Chuvadi.Pdf.Signatures`,
  `Chuvadi.Pdf.PdfA`, `Chuvadi.Pdf.Reader`.
- **`Chuvadi.Pdf`** — a convenience **meta-package**. It has no code of its own;
  installing it pulls in the stack as dependencies, so a consumer who just wants
  "the PDF library" runs one command: `dotnet add package Chuvadi.Pdf`.

`Chuvadi.Pdf.Rendering.Wpf` targets `net10.0-windows` and is a separate,
Windows-only package. It is **not** referenced by the `Chuvadi.Pdf`
meta-package, so the meta-package stays cross-platform.

Not packed: tests, examples, `tools/Chuvadi.Pdf.Cli`, and benchmarks — all marked
`IsPackable=false`.

## Versioning

One version for the whole library (mono-versioning). All packages are stamped
with the same `-p:Version=x.y.z`, which makes the inter-package dependency
versions line up automatically (a `ProjectReference` becomes a package
dependency at the same version). This matches the `git tag vX.Y.Z` release flow.
The current released version is **3.16.0**.

## Build and publish (local feeds)

Chuvadi currently publishes to **local folder feeds only, not nuget.org.**
From the repository root:

```powershell
# 1. Produce every package at a version (cleans bin/obj, builds Release,
#    runs the full test suite, then packs).
.\build\pack.ps1 -Version 3.16.0
#    -> artifacts\nupkg\*.nupkg   (33 packages, all at 3.16.0)

# 2. Publish by FLAT-COPYING the .nupkg files into each local feed.
#    Do NOT use `dotnet nuget push` for the local feeds — it creates a
#    hierarchical id/version layout that the flat-feed consumers do not expect.
Copy-Item artifacts\nupkg\*.nupkg "C:\Users\aruns\Documents\local-nuget\" -Force
Copy-Item artifacts\nupkg\*.nupkg "C:\Users\aruns\Documents\ChuvadiReader\ChuvadiReader\localpackages\" -Force

# 3. Tag the release.
git tag v3.16.0
git push origin v3.16.0
```

`pack.ps1` cleans `bin`/`obj`, builds Release, runs the full test suite (pass
`-NoTest` to skip), then packs the solution.

> The meta-package must be in the solution for the solution-level pack to
> include it. It is already registered in `Chuvadi.slnx`.

## Publishing to nuget.org (not yet enabled)

Public nuget.org publishing is a future step (see `docs/BACKLOG.md`,
distribution housekeeping). Before the first public push, complete these
one-time items:

1. **Project / repository URL.** `Directory.Build.props` sets
   `PackageProjectUrl` and `RepositoryUrl` to
   `https://github.com/arunshivab/chuvadi-pdf` (the public repo). Confirm this
   is the canonical public home — it is shown on nuget.org and drives source
   linking.
2. **Source Link.** Source Link (jump-to-source from the debugger) works only if
   the repository is publicly reachable at that URL. The repo is public, so
   embedded PDBs plus a `Microsoft.SourceLink.GitHub` reference give consumers
   real source debugging.
3. **Reserve the `Chuvadi` ID prefix** on nuget.org so the `Chuvadi.*` and
   `Chuvadi.Pdf.*` package IDs are protected.
4. **Package icon.** Replace the placeholder `icon.png` at the repo root with the
   real project icon (128×128 PNG).
5. **Documentation completeness.** `Directory.Build.props` suppresses `CS1591`
   (missing XML doc warnings) during development. Remove that suppression before
   a 1.0 public release so every public member is documented in the shipped
   packages.
6. **Meta-package coverage.** Reconcile the `Chuvadi.Pdf` meta-package's direct
   dependencies to list the newer modules (Xfa, PdfA, Text.Shaping, Color,
   Rendering.Raster, Rendering.Walking) rather than relying solely on transitive
   resolution.
7. **API key.** Store the nuget.org API key as `NUGET_API_KEY` (environment
   variable / CI secret); never commit it or pass it where it could be logged,
   then push with `--skip-duplicate`.
