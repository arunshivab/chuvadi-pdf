# PdfPage

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Represents a single page in a PDF document.

```csharp
public sealed class PdfPage
```

## Remarks

A page is defined by a page dictionary in the PDF file. `PdfPage` exposes the commonly needed entries: bounding boxes, rotation, resources, and the raw page dictionary for advanced access. Inherited entries (from ancestor /Pages nodes) are resolved by walking up the page tree when a key is absent from this page's dictionary. PDF 32000-1:2008 §7.7.3.3 — Page objects, Table 30.

## Properties

### `StructTreeRoot`

```csharp
PdfDictionary? StructTreeRoot => _structTreeRoot?.Invoke()
```

Gets the structure-tree root of the document this page belongs to (`PdfDocument.StructTreeRoot`), or null when the document has none (since 3.29). Text extraction reads a Tagged PDF's reading order from it. PDF 32000-1:2008 §14.7.2 — Structure Hierarchy.

### `AcroForm`

```csharp
PdfDictionary? AcroForm => _acroForm?.Invoke()
```

Gets the interactive form dictionary (`/AcroForm`) of the document this page belongs to, or null when it has none (3.31). Field appearances are made from its `/DA`, `/DR` and `/NeedAppearances`. PDF 32000-1:2008 §12.7.2 — Interactive Form Dictionary.

### `Index`

```csharp
int Index => _pageIndex
```

Gets the zero-based index of this page in the document.

### `PageNumber`

```csharp
int PageNumber => _pageIndex + 1
```

Gets the one-based page number of this page.

### `BleedBox`

```csharp
PdfRectangle BleedBox => OwnBoxOrCropBox("BleedBox")
```

Gets the BleedBox — the region printed content is clipped to in production (3.31). Defaults to `CropBox`; not inherited. PDF 32000-1:2008 §14.11.2.

### `TrimBox`

```csharp
PdfRectangle TrimBox => OwnBoxOrCropBox("TrimBox")
```

Gets the TrimBox — the intended size of the finished page (3.31). Defaults to `CropBox`; not inherited. PDF 32000-1:2008 §14.11.2.

### `ArtBox`

```csharp
PdfRectangle ArtBox => OwnBoxOrCropBox("ArtBox")
```

Gets the ArtBox — the extent of the page's meaningful content (3.31). Defaults to `CropBox`; not inherited. PDF 32000-1:2008 §14.11.2.

### `Width`

```csharp
double Width => MediaBox.Width
```

Gets the width of the page's MediaBox in points (1/72 inch).

### `Height`

```csharp
double Height => MediaBox.Height
```

Gets the height of the page's MediaBox in points (1/72 inch).

### `Rotate`

```csharp
int Rotate => GetInheritedInteger(PdfName.Rotate, 0)
```

Gets the page rotation in degrees (0, 90, 180, or 270). PDF 32000-1:2008 §7.7.3.3, Table 30 — Rotate.

### `Resources`

```csharp
PdfDictionary? Resources => GetInheritedDictionary(PdfName.Resources)
```

Gets the Resources dictionary for this page, or null when absent. PDF 32000-1:2008 §7.7.3.3, Table 30 — Resources.

### `Dictionary`

```csharp
PdfDictionary Dictionary => _dict
```

Gets the raw page dictionary for advanced access.

### `Contents`

```csharp
PdfPrimitive? Contents => _dict.GetAs<PdfPrimitive>(PdfName.Contents)
```

Gets the /Contents entry as a primitive (may be a reference, an array of references, or null when the page has no content).

### `HasContent`

```csharp
bool HasContent
```

Returns true when the page has at least one non-empty content stream. This is a cheap structural check — a `/Contents` stream is present with non-zero raw length — and does not decode the stream or verify that it paints any visible marks.

## Methods

### `FindViewport`

```csharp
PdfViewport? FindViewport(double x, double y)
```

Returns the viewport that governs a point (default user space): the last one in `Viewports` whose region contains it, as the specification directs (later viewports are on top), or null.

**Parameters**

- `x` — Point x.
- `y` — Point y.

**Returns:** The viewport, or null when none contains the point.

### `MeasureDistance`

```csharp
PdfMeasurement? MeasureDistance(double x1, double y1, double x2, double y2)
```

Measures the distance between two points (default user space) in the real-world units of the viewport containing both, or null when no single viewport with a rectilinear measure contains them.

**Parameters**

- `x1` — First point x.
- `y1` — First point y.
- `x2` — Second point x.
- `y2` — Second point y.

**Returns:** The measured distance, or null.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfPage.cs`](../../../src/Chuvadi.Pdf.Documents/PdfPage.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
