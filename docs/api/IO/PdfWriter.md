# PdfWriter

**Class** in `Chuvadi.Pdf.IO` (IO)

Writes a complete PDF file to an output stream.

```csharp
public static class PdfWriter
```

## Remarks

`PdfWriter` performs a full rewrite — it serialises all provided indirect objects, builds a fresh cross-reference table, and writes a valid PDF trailer. Streams are written with their existing raw bytes unchanged. The `/Length` entry is updated to reflect the actual byte count. PDF version written: `%PDF-1.7`. xref format: classic cross-reference table (not a cross-reference stream). PDF 32000-1:2008 §7.5 — File structure.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, IEnumerable<PdfIndirectObject> objects, PdfDictionary trailer)
```

Writes a complete PDF file containing the given indirect objects.

**Parameters**

- `output` — The stream to write to. Must be writable.
- `objects` — The indirect objects to include. Object 0 (the free list head) is added automatically and must not be included by the caller.
- `trailer` — The trailer dictionary. `/Size` is computed automatically. `/Root` must be set by the caller.

### `WriteLinearized`

__static__

```csharp
static void WriteLinearized(Stream output, IEnumerable<PdfIndirectObject> objects, PdfDictionary trailer)
```

Writes a linearized (Fast Web View) PDF.

**Parameters**

- `output` — Writable output stream.
- `objects` — Indirect objects to write.
- `trailer` — Trailer dictionary with /Root.

**Remarks:** Per ISO 32000-1 Annex F, the output is laid out so that the first page can be rendered after reading only the file's prefix. Encryption is not yet supported in combination with linearization.

### `Write`

__static__

```csharp
static void Write(Stream output, IEnumerable<PdfIndirectObject> objects, PdfDictionary trailer, EncryptionOptions? encryption, SynthesizedMetadata synthesized = SynthesizedMetadata.All, XrefStyle xrefStyle = XrefStyle.Classic)
```

Writes a PDF with optional encryption applied to every string and stream inside the written objects.

**Parameters**

- `output` — Writable, seekable output stream.
- `objects` — Indirect objects to write.
- `trailer` — Trailer dictionary. When `encryption` is supplied, this method appends an /Encrypt entry referencing a newly created encryption dictionary; the trailer must NOT already contain one.
- `encryption` — Encryption configuration. When null, no encryption is applied.
- `synthesized` — Which absent document-level metadata to synthesise. Defaults to `SynthesizedMetadata.All`, matching the standard behaviour; reduce it to suppress synthesis of /Info and/or /Metadata.
- `xrefStyle` — The cross-reference format to emit. Defaults to `XrefStyle.Classic` (a plaintext cross-reference table). `XrefStyle.Stream` packs eligible objects into object streams and writes a compressed cross-reference stream, producing a smaller file (PDF 1.5+). Object streams compose with encryption: each container is encrypted as a whole and the cross-reference stream is left unencrypted, per ISO 32000-1 §7.6.

### `WriteIncrementalUpdate`

__static__

```csharp
static byte[] WriteIncrementalUpdate(byte[] originalPdfBytes, IEnumerable<PdfIndirectObject> updatedObjects, PdfDictionary? trailerOverlay = null)
```

Appends an incremental update section to an existing PDF, returning the resulting bytes. The original bytes are preserved verbatim, so any existing signatures on the source document remain valid.

**Parameters**

- `originalPdfBytes` — The unmodified source PDF.
- `updatedObjects` — Objects to add or modify. Each carries its own ID; reusing an existing ID replaces that object in the document.
- `trailerOverlay` — Optional trailer entries to add or override in the new trailer (e.g. updating `/Info` for modification metadata). `/Size` and `/Prev` are always controlled by the writer.

**Remarks:** ISO 32000-1 §7.5.6 / ISO 32000-2 §7.5.6. The update section consists of:  
 
- The `updatedObjects`, written in declaration order with their existing object numbers. New objects use IDs at or above the source's `/Size`; modifications reuse the original ID and bump the generation if appropriate. 
- An xref table containing entries only for the objects in this update (the obj-0 free entry stays in the original xref section reached via the new trailer's `/Prev`). 
- A trailer dictionary copying `/Root` and `/ID` from the original trailer, with `/Size` auto-computed and `/Prev` set to the original startxref offset. Any entries supplied in `trailerOverlay` are merged on top. 
- `startxref` pointing at this update's xref offset, then `%%EOF`.  

 The catalog cannot be replaced via this method (its identity is established by `/Root` in the original trailer); to change the catalog's contents, include a new `PdfIndirectObject` for the catalog's object ID in `updatedObjects`.

---

_Source: [`src/Chuvadi.Pdf.IO/PdfWriter.cs`](../../../src/Chuvadi.Pdf.IO/PdfWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
