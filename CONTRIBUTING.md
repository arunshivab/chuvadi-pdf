# Contributing to Chuvadi

Thank you for your interest in contributing.

---

## Before You Start

- Check the [open issues](https://github.com/chuvadi/chuvadi/issues)
  to see if your idea or bug is already being tracked.
- For large changes, open an issue first to discuss the approach
  before writing code.

---

## Development Setup

```bash
git clone https://github.com/chuvadi/chuvadi
cd chuvadi
pwsh setup.ps1       # Creates .sln file, restores packages
pwsh build/fetch-fonts.ps1   # Chuvadi.Fonts into .fonts-feed/ (SHA-256 checked)
dotnet build
dotnet test
```

---

## Code Standards

- C# 13+, .NET 10 only
- Nullable reference types: `enable` — no suppressions without justification
- `TreatWarningsAsErrors`: all warnings must be resolved, not suppressed
- Every public API must have XML doc comments
- Every non-trivial method must have unit tests
- Follow the `.editorconfig` — `dotnet format` must pass
- One public type per file, file named after the type
- File-scoped namespaces

---

## Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(filters): implement ASCII85 decoder
fix(parser): handle missing xref offset in linearized PDFs
docs(text): add layout extractor usage examples
test(io): add round-trip tests for object streams
perf(deflate): use stackalloc for small output buffers
```

Types: `feat`, `fix`, `docs`, `test`, `perf`, `refactor`, `chore`

---

## Pull Request Process

1. Fork the repository
2. Create a branch: `git checkout -b feat/your-feature`
3. Write tests first, then implementation
4. Ensure `dotnet test` passes on all platforms
5. Ensure `dotnet format --verify-no-changes` passes
6. Open a pull request against `main`
7. Describe what you changed and why

---

## PDF Specification References

The PDF specification (ISO 32000-2:2020) is the authoritative source.
Adobe's freely available PDF 1.7 reference covers most of what we need:
https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf

When implementing a feature, cite the relevant spec section in a comment:
```csharp
// PDF 32000-1:2008 §7.3.8.1 — Stream objects
```

---

## No External Dependencies

The `src/` projects must have zero NuGet dependencies.
Test projects may use xUnit, FluentAssertions, FsCheck, and BenchmarkDotNet.
If you think an exception is warranted, open an issue to discuss first.

---

## Code of Conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
