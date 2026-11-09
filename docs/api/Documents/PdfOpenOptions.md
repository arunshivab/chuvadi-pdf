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

### `OnWarning`

```csharp
System.Action<PdfWarning>? OnWarning
```

Gets the receiver of warnings. Where Chuvadi meets something it cannot use and carries on without it (a damaged embedded font replaced by a substitute, a damaged glyph that draws nothing, a shading that is skipped), it calls this with a `PdfWarning` saying what, on which page and why. It is kept on the document (`PdfDocument.OnWarning`) and called on the thread doing the work, so a handler shared between threads must be thread-safe. An exception it throws passes to the caller of that work. Null: warnings are not reported.

### `RepairIfDamaged`

```csharp
bool RepairIfDamaged
```

Gets a value indicating whether to repair a damaged file while opening it (3.32.1, D-280). When true and the file's structure cannot be read (a wrong `startxref`, a broken cross-reference table or trailer, junk before the header, a truncated file), the file is repaired in memory with `PdfRepairer` and the repaired copy is opened. The file on disk is not changed. `PdfDocument.Warnings` then starts with a line saying the file was repaired, why, and what was recovered (so `PdfDocument.IsRecovered` is true). Default false: such a file throws `PdfParseException`, as before.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfOpenOptions.cs`](../../../src/Chuvadi.Pdf.Documents/PdfOpenOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
