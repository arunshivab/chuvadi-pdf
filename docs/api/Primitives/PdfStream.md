# PdfStream

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF stream object — a dictionary plus a binary byte payload.

```csharp
public sealed class PdfStream : PdfPrimitive
```

## Remarks

A stream consists of a dictionary (which must contain a `/Length` entry) followed by the keywords `stream` and `endstream` surrounding the raw byte data. Streams may be compressed with one or more filters specified in the `/Filter` entry. The raw bytes stored here are the bytes as they appear in the file — i.e., possibly still compressed. Filter application and decompression happen in `Chuvadi.Pdf.Filters`. PDF 32000-1:2008 §7.3.8 — Stream objects.

## Constructors

### `PdfStream(PdfDictionary dictionary, ReadOnlySpan<byte> rawBytes)`

Initialises a new `PdfStream` with the given dictionary and raw (possibly compressed) bytes.

**Parameters**

- `dictionary` — The stream dictionary. Must not be null. A reference is kept — the dictionary is not copied.
- `rawBytes` — The raw byte content as it appears in the PDF file, before any filter decoding. A copy is taken.

## Properties

### `Dictionary`

```csharp
PdfDictionary Dictionary
```

Gets the stream's dictionary.

### `RawBytes`

```csharp
byte[] RawBytes
```

Gets the raw byte content as it appears in the PDF file, before any filter decoding.

### `RawLength`

```csharp
int RawLength => RawBytes.Length
```

Gets the number of raw bytes in this stream. Equivalent to `RawBytes.Length`.

### `Filter`

```csharp
PdfPrimitive? Filter => Dictionary.GetAs<PdfPrimitive>(PdfName.Filter)
```

Gets the `/Filter` entry from the stream dictionary, which identifies the compression filter(s) applied to the data. Returns `null` if the stream is uncompressed.

### `IsFiltered`

```csharp
bool IsFiltered => Filter is not null
```

Returns true when the stream has at least one filter applied.

### `RawSpan`

```csharp
ReadOnlySpan<byte> RawSpan => RawBytes
```

Gets a read-only span over the raw bytes. Preferred over `RawBytes` for processing — avoids pinning the array and communicates read-only intent.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Stream
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfStream.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfStream.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
