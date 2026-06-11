# ImagePageSizing

**Enum** in `Chuvadi.Pdf.Authoring` (Authoring)

How `ImagePdfConverter` sizes each PDF page relative to its image.

```csharp
public enum ImagePageSizing
```

## Values

| Name | Description |
|---|---|
| `SizeToImage` | The page is exactly the image's size at `ImagePdfOptions.Dpi` (page points = pixels × 72 ÷ DPI). No margins; the image fills the page. |
| `FitToPage` | The page is a fixed paper size (`ImagePdfOptions.PageSize`); the image is scaled to fit inside the margins, preserving aspect ratio. |

---

_Source: [`src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs`](../../../src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
