# PdfPage

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Represents a single page in a PDF document.

```csharp
public sealed class PdfPage
```

## Remarks

A page is defined by a page dictionary in the PDF file. `PdfPage` exposes the commonly needed entries: bounding boxes, rotation, resources, and the raw page dictionary for advanced access. Inherited entries (from ancestor /Pages nodes) are resolved by walking up the page tree when a key is absent from this page's dictionary. PDF 32000-1:2008 §7.7.3.3 — Page objects, Table 30.

## Properties

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

### `Dictionary`

```csharp
PdfDictionary Dictionary => _dict
```

Gets the raw page dictionary for advanced access.

### `HasContent`

```csharp
bool HasContent
```

Returns true when the page has at least one non-empty content stream. This is a cheap structural check — a `/Contents` stream is present with non-zero raw length — and does not decode the stream or verify that it paints any visible marks.

## Methods

### `GetInheritedInteger`

```csharp
int Rotate => GetInheritedInteger(PdfName.Rotate, 0)
```

Gets the page rotation in degrees (0, 90, 180, or 270). PDF 32000-1:2008 §7.7.3.3, Table 30 — Rotate.

### `GetInheritedDictionary`

```csharp
PdfDictionary? Resources => GetInheritedDictionary(PdfName.Resources)
```

Gets the Resources dictionary for this page, or null when absent. PDF 32000-1:2008 §7.7.3.3, Table 30 — Resources.

### `_dict.GetAs<PdfPrimitive>`

```csharp
PdfPrimitive? Contents => _dict.GetAs<PdfPrimitive>(PdfName.Contents)
```

Gets the /Contents entry as a primitive (may be a reference, an array of references, or null when the page has no content).

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfPage.cs`](../../../src/Chuvadi.Pdf.Documents/PdfPage.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
