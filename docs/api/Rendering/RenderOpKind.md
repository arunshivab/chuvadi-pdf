# RenderOpKind

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Tag identifying the concrete `RenderOp` subtype.

```csharp
public enum RenderOpKind
```

## Values

| Name | Description |
|---|---|
| `Path` | `PathOp`. |
| `Text` | `TextOp`. |
| `Image` | `ImageOp`. |
| `Clip` | `ClipOp`. |
| `Transform` | `TransformOp`. |
| `Opacity` | `OpacityOp`. |
| `BlendMode` | `BlendModeOp`. |
| `Shading` | `ShadingOp`. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
