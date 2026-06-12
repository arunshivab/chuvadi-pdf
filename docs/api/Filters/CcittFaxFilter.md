# CcittFaxFilter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the `CCITTFaxDecode` filter: Group 3 one-dimensional (Modified Huffman), Group 3 two-dimensional (Modified READ), and Group 4 (Modified Modified READ) decoding of bilevel image data, as used by scanned-document PDFs.

```csharp
public sealed class CcittFaxFilter : IStreamFilter
```

## Remarks

The output is packed one-bit-per-pixel rows, most significant bit first, each row padded to a byte boundary. With the default `BlackIs1 = false`, black pixels decode to 0 bits and white to 1 bits, per PDF 32000-1:2008 Table 11.  

 Encoding is not supported; `Encode` throws `FilterException`. Chuvadi writes bilevel images with Flate, which modern consumers handle universally.

## Properties

### `FilterName`

```csharp
string FilterName => "CCITTFaxDecode"
```

<inheritdoc />

## Methods

### `Decode`

```csharp
void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
```

<inheritdoc />

### `Encode`

```csharp
void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Filters/CcittFaxFilter.cs`](../../../src/Chuvadi.Pdf.Filters/CcittFaxFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
