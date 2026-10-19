# PdfReader

**Class** in `Chuvadi.Pdf.IO` (IO)

Opens an existing PDF file and provides access to its object graph. PDF 32000-1:2008 §7.5 — File structure.

```csharp
public sealed class PdfReader : IDisposable
```

## Properties

### `Trailer`

```csharp
PdfDictionary Trailer
```

Gets the PDF trailer dictionary.

### `Objects`

```csharp
PdfObjectStore Objects
```

Gets the lazy object store.

## Methods

### `Open`

__static__

```csharp
static PdfReader Open(Stream stream, bool leaveOpen = false)
```

Opens a PDF file from the given readable, seekable stream.

**Remarks:** Performs synchronous blocking I/O against `stream`. The reader holds a reference to the stream for the lifetime of the document and reads objects lazily on demand. Memory-efficient for large files because only xref tables and accessed objects are materialised. Not supported on WebAssembly (browser blocks on synchronous I/O against network resources). Use `OpenAsync(Stream, CancellationToken)` for cross-platform code. <exception cref="PdfParseException"> Thrown when the file is encrypted. Use the password overload to open encrypted PDFs. </exception>

### `Open`

__static__

```csharp
static PdfReader Open(Stream stream, string password, bool leaveOpen = false)
```

Opens a PDF file with the given password. For unencrypted PDFs the password is ignored.

**Parameters**

- `stream` — Readable, seekable stream containing the PDF.
- `password` — User or owner password. Empty string for default.
- `leaveOpen` — Whether to leave the stream open on dispose. <exception cref="PdfParseException">Thrown when the password is incorrect.</exception>

**Remarks:** Synchronous blocking I/O. Not supported on WebAssembly; use `OpenAsync(Stream, string, CancellationToken)` for cross-platform code.

### `ReadFileBytes`

```csharp
byte[] ReadFileBytes(long offset, int count)
```

Reads a contiguous byte range directly from the underlying PDF file.

**Parameters**

- `offset` — Absolute byte offset within the PDF file.
- `count` — Number of bytes to read.

**Returns:** A newly allocated byte array of length `count`.

**Remarks:** Signature verification needs the raw bytes of the file at known offsets — the signature dictionary's /ByteRange entry identifies them. This method exposes that capability without requiring callers to keep their own copy of the file.

### `CopyFileBytes`

```csharp
void CopyFileBytes(long offset, long count, Stream destination)
```

Copies a contiguous byte range from the file directly into `destination`.

**Remarks:** Use this for hash computation over large byte ranges — feeds the destination stream incrementally without materialising the bytes as a single array.

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

### `LoadXref`

__static__

```csharp
static XrefTable LoadXref(Stream stream)
```

Locates the file's `startxref` offset, walks the complete cross-reference chain (classic xref tables and/or xref streams, following `/Prev` links), and returns the merged cross-reference table.

**Parameters**

- `stream` — A readable, seekable stream positioned anywhere; this method seeks as needed and leaves the stream at an unspecified position.

**Returns:** The merged `XrefTable` for the whole file. <exception cref="ArgumentNullException">`stream` is null.</exception> <exception cref="PdfParseException"> The `startxref` keyword or a valid offset could not be found, or an xref section could not be parsed. </exception>

---

_Source: [`src/Chuvadi.Pdf.IO/PdfReader.cs`](../../../src/Chuvadi.Pdf.IO/PdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
