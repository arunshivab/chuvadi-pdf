# ImageDecoding

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Decodes a display-list `ImageOp` into the pixels the raster route paints for it, through the same decoder the raster renderer uses, so the two outputs stay identical.

```csharp
public static class ImageDecoding
```

## Methods

### `Decode`

__static__

```csharp
static DecodedImage Decode(ImageOp image)
```

The pixels of `image` as the raster route paints them: 8-bit straight (not premultiplied) RGBA in sRGB, top row first, in the orientation of the raster route's image frame. Every colour space, bit depth, `/Decode`, `/SMask` (on JPEGs too), `/Mask` (stencil and colour key) and stencil mask the raster route draws is covered; a stencil mask takes the op's `ImageOp.StencilColor`. When the raster route would not draw the image (a 16-bit sample, Lab, Separation, DeviceN, JPEG 2000 and so on), the result has no pixels and the same reason text the raster route reports.

**Parameters**

- `image` — An image op from a display list built by `Chuvadi.Pdf.Rendering.DisplayList.DisplayListBuilder`.

**Returns:** The decoded image, or the reason it is not drawn. <exception cref="ArgumentNullException">Thrown when `image` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Rendering/ImageDecoding.cs`](../../../src/Chuvadi.Pdf.Rendering/ImageDecoding.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
