# Jbig2Filter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

The `JBIG2Decode` filter (PDF 32000-1:2008 §7.4.7). Decodes an embedded JBIG2 stream's page bitmap and emits packed 1-bit-per-pixel image data, one byte-aligned row at a time. JBIG2's native sense is 1 = black; PDF image data expects 0 = black for a 1-bpp DeviceGray sample, so the packed bits are inverted on output.

```csharp
public sealed class Jbig2Filter : IStreamFilter
```

## Remarks

This release decodes arithmetic-coded generic regions, symbol dictionaries, and text regions. Shared segments named by the image's `/JBIG2Globals` entry are supplied through `FilterParameters.Jbig2Globals` by the decoding call site. Huffman-coded segments, refinement/aggregate coding, transposed text regions, and MMR-coded generic regions are not yet supported and raise a `FilterException` where encountered.

## Properties

### `FilterName`

```csharp
string FilterName => "JBIG2Decode"
```

<inheritdoc />

## Methods

### `Encode`

```csharp
void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
```

<inheritdoc />

### `Decode`

```csharp
void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Filters/Jbig2Filter.cs`](../../../src/Chuvadi.Pdf.Filters/Jbig2Filter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
