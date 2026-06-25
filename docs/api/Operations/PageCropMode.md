# PageCropMode

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

Selects how `PageCropper` confines a page to its crop rectangle.

```csharp
public enum PageCropMode
```

## Values

| Name | Description |
|---|---|
| `ClipOnly` | Lossless visual crop: the page boxes are reset and existing content is wrapped in a hard clip. In-box content is preserved byte-for-byte; off-box bytes remain in the file but are clipped from view (not removed). |
| `Scrub` | Redaction-grade crop: off-box vector geometry is physically removed, boundary-crossing geometry is clipped to its in-box portion, off-box text glyphs are dropped, and boundary-crossing images are cropped to the in-box region. In-box content is preserved. Boundary-crossing geometry is flattened where it must be clipped. |

---

_Source: [`src/Chuvadi.Pdf.Operations/PageCropMode.cs`](../../../src/Chuvadi.Pdf.Operations/PageCropMode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
