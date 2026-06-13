# JpegDecoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Decodes baseline sequential (SOF0) and progressive (SOF2) DCT JPEG images into an `ImageFrame`. Supports 8-bit precision with 1 component (grayscale), 3 components (YCbCr or RGB), and 4 components (CMYK or YCCK, using the Adobe APP14 colour transform). Chroma subsampling and restart intervals are handled.

```csharp
public static class JpegDecoder
```

## Remarks

Not supported: 12-bit precision, arithmetic coding (SOF9–SOF11), and lossless modes. CMYK/YCCK output is converted to RGB for display, honouring the Adobe inverted-channel convention.

## Methods

### `Decode`

__static__

```csharp
static ImageFrame Decode(byte[] data)
```

Decodes a JPEG from a byte array. <exception cref="ArgumentNullException">When `data` is null.</exception> <exception cref="ImageException">When the JPEG is invalid or unsupported.</exception>

### `Decode`

__static__

```csharp
static ImageFrame Decode(Stream input)
```

Decodes a JPEG from a stream. <exception cref="ArgumentNullException">When `input` is null.</exception> <exception cref="ImageException">When the JPEG is invalid or unsupported.</exception>

---

_Source: [`src/Chuvadi.Pdf.Images/JpegDecoder.cs`](../../../src/Chuvadi.Pdf.Images/JpegDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
