# PdfAResult

**Class** in `Chuvadi.Pdf.PdfA` (PdfA)

The outcome of a PDF/A write attempt.

```csharp
public sealed class PdfAResult
```

## Properties

### `Succeeded`

```csharp
bool Succeeded
```

True when a conforming file was written. When false, nothing was written to the output stream and `Violations` explains why.

### `Violations`

```csharp
IReadOnlyList<string> Violations
```

Messages describing conformance problems that could not be fixed.

---

_Source: [`src/Chuvadi.Pdf.PdfA/PdfAOptions.cs`](../../../src/Chuvadi.Pdf.PdfA/PdfAOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
