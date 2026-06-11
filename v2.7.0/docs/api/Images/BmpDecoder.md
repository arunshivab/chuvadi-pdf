# BmpDecoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Decodes a Windows BMP image into an `ImageFrame`.

```csharp
public static class BmpDecoder
```

## Remarks

Supports: 
 
- Headers: BITMAPCOREHEADER (12 bytes), BITMAPINFOHEADER (40 bytes), and the V2–V5 extensions (52, 56, 108, 124 bytes). 
- Bit depths: 1, 4, 8 (palette), 16 (5-5-5 default or BI_BITFIELDS masks), 24 (BGR), 32 (BGRX, or masked channels including alpha under BI_BITFIELDS). 
- Compression: BI_RGB (uncompressed), BI_RLE8, BI_RLE4, BI_BITFIELDS. 
- Row order: bottom-up (positive height) and top-down (negative height).  The decoder is the inverse companion of `BmpEncoder`; together they round-trip 24-bit BI_RGB bitmaps losslessly.

## Methods

### `Decode`

__static__

```csharp
static ImageFrame Decode(byte[] data)
```

Decodes a BMP from a byte array.

**Parameters**

- `data` — The raw BMP bytes.

**Returns:** A decoded `ImageFrame`. <exception cref="ArgumentNullException">`data` is null.</exception> <exception cref="ImageException">Thrown on invalid or unsupported BMP data.</exception>

### `Decode`

__static__

```csharp
static ImageFrame Decode(Stream input)
```

Decodes a BMP from a stream.

**Parameters**

- `input` — The stream positioned at the start of the BMP data.

**Returns:** A decoded `ImageFrame`. <exception cref="ArgumentNullException">`input` is null.</exception> <exception cref="ImageException">Thrown on invalid or unsupported BMP data.</exception>

---

_Source: [`src/Chuvadi.Pdf.Images/BmpDecoder.cs`](../../../src/Chuvadi.Pdf.Images/BmpDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
