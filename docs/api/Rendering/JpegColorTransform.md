# JpegColorTransform

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The colour transform of a JPEG image (its Adobe APP14 marker, if any).

```csharp
public enum JpegColorTransform
```

## Values

| Name | Description |
|---|---|
| `None` | No transform recorded, or not a JPEG image. |
| `YCbCr` | YCbCr (APP14 transform 1), or the JFIF default for 3 components. |
| `Ycck` | YCCK (APP14 transform 2): a CMYK JPEG stored as YCCK. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
