# PdfDisplayBox

**Enum** in `Chuvadi.Pdf.Documents` (Documents)

Which page box the raster, SVG and WPF renderers show (3.32.0).

```csharp
public enum PdfDisplayBox
```

## Values

| Name | Description |
|---|---|
| `CropBox` | The CropBox, intersected with the MediaBox (PDF 32000-1:2008 §14.11.2): what a viewer shows and a printer prints. The default since 3.32.0. A page without a CropBox shows its MediaBox, as before. |
| `MediaBox` | The whole MediaBox, as every release before 3.32.0 rendered it. |

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfDisplayBox.cs`](../../../src/Chuvadi.Pdf.Documents/PdfDisplayBox.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
