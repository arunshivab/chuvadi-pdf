# PageComposer

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Builds a new PDF by placing pages from existing documents onto target sheets under arbitrary affine transforms. Each placed page is imported as a form XObject, so vector and text content stay intact and selectable (not rasterised). One `PlacePage` per sheet covers rotate-any-angle and resize; several per sheet cover N-up and imposition.

```csharp
public sealed class PageComposer
```

## Remarks

The supplied `Transform` maps the source page's coordinate space to the target sheet's coordinate space (PDF default user space, origin bottom-left). The source page's content is placed in its native (un-rotated) coordinates; use `PdfPage.EffectiveSize` to size and position against the page as displayed.

## Methods

### `AddPage`

```csharp
PageComposer AddPage(PageSize size)
```

Adds a blank target sheet of a standard or custom size.

### `AddPage`

```csharp
PageComposer AddPage(double width, double height)
```

Adds a blank target sheet of arbitrary dimensions (points).

### `AddPageMatching`

```csharp
PageComposer AddPageMatching(PdfDocument source, int sourcePageIndex)
```

Adds a blank target sheet sized to a source page's displayed size (crop box, accounting for `PdfPage.Rotate`).

### `PlacePage`

```csharp
PageComposer PlacePage(PdfDocument source, int sourcePageIndex, Transform transform)
```

Places a source page onto the current target sheet under the given transform. Call repeatedly to compose several pages onto one sheet.

### `Write`

```csharp
void Write(Stream output)
```

Writes the composed document to `output`.

---

_Source: [`src/Chuvadi.Pdf.Operations/PageComposer.cs`](../../../src/Chuvadi.Pdf.Operations/PageComposer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
