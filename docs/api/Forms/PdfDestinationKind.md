# PdfDestinationKind

**Enum** in `Chuvadi.Pdf.Forms` (Forms)

How a destination positions the page in the window (PDF 32000-1:2008 Table 151).

```csharp
public enum PdfDestinationKind
```

## Values

| Name | Description |
|---|---|
| `XYZ` | `/XYZ`: `PdfDestination.Left` and `PdfDestination.Top` at the window's upper-left corner, magnified by `PdfDestination.Zoom`. Also used for a destination whose kind is missing or not recognised, with every value null ("keep the current view"). |
| `Fit` | `/Fit`: the whole page fits the window. |
| `FitH` | `/FitH`: the page width fits, with `PdfDestination.Top` at the top. |
| `FitV` | `/FitV`: the page height fits, with `PdfDestination.Left` at the left. |
| `FitR` | `/FitR`: the rectangle Left, Bottom, Right, Top fits the window. |
| `FitB` | `/FitB`: the page's bounding box fits the window. |
| `FitBH` | `/FitBH`: the bounding box width fits, with `PdfDestination.Top` at the top. |
| `FitBV` | `/FitBV`: the bounding box height fits, with `PdfDestination.Left` at the left. |

---

_Source: [`src/Chuvadi.Pdf.Forms/PdfDestination.cs`](../../../src/Chuvadi.Pdf.Forms/PdfDestination.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
