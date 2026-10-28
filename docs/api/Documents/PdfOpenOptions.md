# PdfOpenOptions

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Options for opening a PDF with `PdfDocument.Open(System.IO.Stream, PdfOpenOptions)`.

```csharp
public sealed class PdfOpenOptions
```

## Properties

### `Password`

```csharp
string? Password
```

Gets the user or owner password. Null or empty: open without one (an encrypted file that needs one throws `PdfPasswordRequiredException`).

### `Limits`

```csharp
PdfLimits? Limits
```

Gets the resource limits. `PdfLimits.MaxInputBytes` and `PdfLimits.MaxPageCount` are checked while opening; all of them are kept on the document (`PdfDocument.Limits`) and applied by `PageRasterizer` and `TextExtractor`. Null: none.

### `LeaveOpen`

```csharp
bool LeaveOpen
```

Gets a value indicating whether to leave the stream open when the document is disposed. Ignored by the asynchronous and path overloads, which never own the caller's stream.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfOpenOptions.cs`](../../../src/Chuvadi.Pdf.Documents/PdfOpenOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
