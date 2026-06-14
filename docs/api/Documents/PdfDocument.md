# PdfDocument

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Represents an opened PDF document.

```csharp
public sealed class PdfDocument : IDisposable
```

## Remarks

`PdfDocument` wraps a `PdfReader` and exposes the document-level object model: pages, metadata, and the document catalog. Open a document with `Open(Stream, bool)` or `Open(string)` on desktop runtimes, or `OpenAsync(Stream, CancellationToken)` / `OpenAsync(string, CancellationToken)` on WebAssembly or any caller that needs to integrate with asynchronous I/O. Dispose the document when finished — it owns the underlying reader and stream. PDF 32000-1:2008 §7.7.2 — Document Catalog. PDF 32000-1:2008 §14.3.3 — Document information dictionary.

## Properties

### `PageCount`

```csharp
int PageCount => Pages.Count
```

Gets the total number of pages.

### `Trailer`

```csharp
PdfDictionary Trailer => _reader.Trailer
```

Gets the raw trailer dictionary.

### `IsLinearized`

```csharp
bool IsLinearized => Linearization is not null
```

Returns true when the document is linearized (Fast Web View).

### `Info`

```csharp
PdfDictionary? Info => _reader.Info
```

Gets the raw document information dictionary, or null when absent.

### `Objects`

```csharp
PdfObjectStore Objects => _reader.Objects
```

Gets the underlying object store for direct object access.

### `Reader`

```csharp
PdfReader Reader => _reader
```

Gets the underlying `PdfReader` for low-level access such as reading raw file bytes for signature byte-range extraction.

## Methods

### `Open`

__static__

```csharp
static PdfDocument Open(Stream stream, bool leaveOpen = false)
```

Opens a PDF document from the given stream.

**Parameters**

- `stream` — A readable, seekable PDF stream.
- `leaveOpen` — True to leave the stream open when this document is disposed.

**Remarks:** Synchronous blocking I/O. Not supported on WebAssembly; use `OpenAsync(Stream, CancellationToken)` for cross-platform code.

### `Open`

__static__

```csharp
static PdfDocument Open(Stream stream, string password, bool leaveOpen = false)
```

Opens an encrypted PDF using the given user or owner password.

**Parameters**

- `stream` — Readable, seekable PDF stream.
- `password` — User or owner password. Empty string for default.
- `leaveOpen` — Whether to leave the underlying stream open on dispose.

**Remarks:** Synchronous blocking I/O. Not supported on WebAssembly; use `OpenAsync(Stream, string, CancellationToken)` for cross-platform code.

### `Open`

__static__

```csharp
static PdfDocument Open(string path, string password)
```

Opens an encrypted PDF from a file path using the given password.

**Remarks:** Synchronous blocking I/O against the file system. Use `OpenAsync(string, string, CancellationToken)` for cross-platform code.

### `Open`

__static__

```csharp
static PdfDocument Open(string path)
```

Opens a PDF document from a file path.

**Parameters**

- `path` — The path to the PDF file.

**Remarks:** Synchronous blocking I/O against the file system. Use `OpenAsync(string, CancellationToken)` for cross-platform code.

### `GetInfoString`

```csharp
string? Title => GetInfoString(PdfName.Intern("Title"))
```

Gets the document Title, or null when not set. PDF 32000-1:2008 §14.3.3, Table 317 — Title.

### `GetInfoString`

```csharp
string? Author => GetInfoString(PdfName.Intern("Author"))
```

Gets the document Author, or null when not set.

### `GetInfoString`

```csharp
string? Subject => GetInfoString(PdfName.Intern("Subject"))
```

Gets the document Subject, or null when not set.

### `GetInfoString`

```csharp
string? Keywords => GetInfoString(PdfName.Intern("Keywords"))
```

Gets the document Keywords, or null when not set.

### `GetInfoString`

```csharp
string? Creator => GetInfoString(PdfName.Intern("Creator"))
```

Gets the name of the application that created the document.

### `GetInfoString`

```csharp
string? Producer => GetInfoString(PdfName.Intern("Producer"))
```

Gets the name of the PDF producer application.

### `TryParseDate`

```csharp
DateTimeOffset? CreationDate => TryParseDate(GetInfoString(PdfName.Intern("CreationDate")))
```

Gets the date and time the document was created, or null when not set or unparsable. PDF 32000-1:2008 §14.3.3, Table 317 — CreationDate.

**Remarks:** The /CreationDate entry is a PDF date string per §7.9.4 of the form `D:YYYYMMDDHHmmSSOHH'mm'`. Missing trailing fields default to zero; a missing timezone offset is treated as UTC.

### `TryParseDate`

```csharp
DateTimeOffset? ModDate => TryParseDate(GetInfoString(PdfName.Intern("ModDate")))
```

Gets the date and time the document was last modified, or null when not set or unparsable. PDF 32000-1:2008 §14.3.3, Table 317 — ModDate.

**Remarks:** Same format and parsing semantics as `CreationDate`.

### `GetInfoName`

```csharp
string? Trapped => GetInfoName(PdfName.Intern("Trapped"))
```

Gets the /Trapped entry indicating whether the document has been modified to include trapping information.

**Remarks:** Returns the name as a string (typically `"True"`, `"False"`, or `"Unknown"`) or null when absent. Some producers erroneously store this entry as a PDF string instead of a PDF name; both forms are accepted.

### `GetXmpMetadata`

```csharp
byte[]? XmpMetadata => GetXmpMetadata()
```

Gets the XMP metadata stream bytes, or null when the document has no /Metadata entry in its Catalog. PDF 32000-1:2008 §14.3.2 — Metadata streams.

**Remarks:** Returns the raw stream bytes as they appear in the file. The XMP specification recommends that metadata streams be uncompressed for searchability; if a producer has chosen to apply a filter, the returned bytes will be in their filtered form. Callers needing the decoded form can read `Catalog`'s /Metadata entry directly and apply the appropriate filter.

### `HasXfaForm`

```csharp
bool IsXfa => HasXfaForm()
```

Returns true when the document is an XFA form, i.e. its `/AcroForm` dictionary carries an `/XFA` entry. XFA content lives outside standard page content, so rendering such a document produces an essentially empty page; consumers can use this flag to show a notice instead of a blank page.

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfDocument.cs`](../../../src/Chuvadi.Pdf.Documents/PdfDocument.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
