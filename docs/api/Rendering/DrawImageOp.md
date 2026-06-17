# DrawImageOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Paints a decoded image at the position specified by a transformation matrix.

```csharp
public sealed class DrawImageOp : RenderOp
```

## Remarks

Emitted by the builder for the PDF Do operator when the named XObject has Subtype /Image. Inline images (BI/ID/EI) emit the same op type.  

 In PDF, an image XObject is conceptually drawn in the unit square (0,0)–(1,1) in image space. The transform that maps unit-square corners to user-space corners is the CTM that was in effect at the Do operator. `DeviceTransform` captures that CTM directly, so the painter can place and rotate/skew/scale the image as the PDF intends without needing to track CTM state.  

 Image colour conversion (CMYK to RGB, indexed colour expansion, etc.) is performed at decode time. `ImageFrame.Pixels` is always in `PixelBuffer` BGRA format regardless of the original source.

## Properties

### `Image`

```csharp
ImageFrame Image
```

Gets the decoded image frame (BGRA pixels regardless of source format).

### `DeviceTransform`

```csharp
Transform DeviceTransform
```

Gets the device-placement transform that maps the image's unit square (0,0)–(1,1) into PDF user space.

### `Alpha`

```csharp
double Alpha
```

Gets the constant image opacity from ExtGState /ca (0..1). Multiplies the per-pixel alpha (including any /SMask) during compositing.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DrawImageOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DrawImageOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
