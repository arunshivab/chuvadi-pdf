# SanitizeOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options for `PdfSanitizer`. The defaults disarm everything.

```csharp
public sealed class SanitizeOptions
```

## Properties

### `RemoveEmbeddedFiles`

```csharp
bool RemoveEmbeddedFiles
```

Gets a value indicating whether embedded files leave the document: the `/EmbeddedFiles` name tree, associated files (`/AF`), the embedded streams of file specifications (`/EF`, `/RF`), portfolios (`/Collection`) and file-attachment annotations. Default true.

### `RemoveXfa`

```csharp
bool RemoveXfa
```

Gets a value indicating whether the XFA form packets (which carry scripts) leave the AcroForm. A hybrid form keeps its AcroForm fields and appearances; an XFA-only form loses its dynamic layout. Default true.

### `RemoveMultimedia`

```csharp
bool RemoveMultimedia
```

Gets a value indicating whether multimedia annotations (`RichMedia`, `Screen`, `Movie`, `Sound`, `3D`) leave the pages. Default true.

### `KeepUriLinks`

```csharp
bool KeepUriLinks
```

Gets a value indicating whether `URI` link actions are kept (they open a web address only when the reader clicks the link). Default false: removed, so the output never points anywhere outside itself.

### `AllowEncryptedInput`

```csharp
bool AllowEncryptedInput
```

Gets a value indicating whether an encrypted document may be sanitized. The output is written without encryption. Default false: an encrypted document throws `OperationsException`.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfSanitizer.cs`](../../../src/Chuvadi.Pdf.Operations/PdfSanitizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
