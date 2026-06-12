# JpegEncoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Encodes images as baseline sequential JPEG (SOF0) with JFIF headers.

```csharp
public static class JpegEncoder
```

## Remarks

Colour images encode as YCbCr with 4:4:4 sampling (no chroma subsampling) — slightly larger files than 4:2:0 in exchange for clean edges on rasterised text, which is the dominant content this encoder serves. Grayscale frames (`ImageColorFormat.Gray8`) encode as single-component JPEGs. Alpha is ignored (JPEG carries no alpha); CMYK frames are not supported and throw.  

 The quality parameter follows the Independent JPEG Group convention: 1 (worst) to 100 (best), scaling the Annex K reference quantisation tables. The default of 85 matches common screenshot/export quality.

## Methods

### `Encode`

__static__

```csharp
static void Encode(ImageFrame frame, Stream output, int quality = 85)
```

Encodes the frame as a baseline JFIF JPEG and writes it to the stream.

**Parameters**

- `frame` — The image to encode. Alpha channels are ignored.
- `output` — The destination stream.
- `quality` — Quality from 1 (smallest, worst) to 100 (largest, best), IJG convention. Default 85. <exception cref="ArgumentNullException"> Thrown when `frame` or `output` is null. </exception> <exception cref="ArgumentOutOfRangeException"> Thrown when `quality` is outside 1–100. </exception> <exception cref="ImageException"> Thrown when the frame's colour format cannot be represented in JPEG (e.g. `ImageColorFormat.Cmyk32`). </exception>

---

_Source: [`src/Chuvadi.Pdf.Images/JpegEncoder.cs`](../../../src/Chuvadi.Pdf.Images/JpegEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
