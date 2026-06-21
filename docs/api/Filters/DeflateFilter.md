# DeflateFilter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the PDF FlateDecode filter using zlib-framed DEFLATE.

```csharp
public sealed class DeflateFilter : IStreamFilter
```

## Remarks

PDF FlateDecode streams are compressed using the zlib format (RFC 1950), which wraps a DEFLATE-compressed payload (RFC 1951) with a 2-byte header and a 4-byte Adler-32 checksum trailer. This implementation includes: 
 
- Full zlib envelope handling (header validation, checksum verification) 
- All three DEFLATE block types: stored (00), fixed Huffman (01), dynamic Huffman (10) 
- PNG predictor reversal (predictors 10-15) for cross-reference streams and image data 
- TIFF predictor reversal (predictor 2) for legacy streams  Compression (Encode) uses fixed Huffman coding for simplicity and correctness. Decompression (Decode) supports all valid DEFLATE streams. PDF 32000-1:2008 §7.4.4. RFC 1950 §2-3 — zlib format. RFC 1951 §3 — DEFLATE format.

## Constructors

### `DeflateFilter(DeflateEffort effort = DeflateEffort.Default)`

Initialises a `DeflateFilter`.

**Parameters**

- `effort` — Encoder effort. `DeflateEffort.Default` uses the fast greedy-parse fixed/dynamic-Huffman path. `DeflateEffort.Maximum` additionally tries the BCL deflater and an iterated optimal ("zopfli-style") parse, keeping whichever candidate is smallest — at the cost of speed.

## Properties

### `FilterName`

```csharp
string FilterName => "FlateDecode"
```

<inheritdoc/>

## Methods

### `Decode`

```csharp
void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
```

<inheritdoc/>

### `Encode`

```csharp
void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Filters/DeflateFilter.cs`](../../../src/Chuvadi.Pdf.Filters/DeflateFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
