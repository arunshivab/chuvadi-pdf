# PageStamper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Stamps a source page onto one or more existing pages of a target document under an affine transform, preserving the rest of the document. The source page is imported once as a form XObject and reused across target pages, so stamping a logo or letterhead onto every page is cheap. Existing content is isolated in its own graphics-state scope so stamps are unaffected by it.

```csharp
public static class PageStamper
```

## Methods

### `Place`

__static__

```csharp
static void Place(Stream output, PdfDocument target, int targetPageIndex, PdfDocument source, int sourcePageIndex, Transform transform, StampPlacement placement = StampPlacement.Overlay)
```

Stamps a source page onto a single target page.

### `PlaceOnAll`

__static__

```csharp
static void PlaceOnAll(Stream output, PdfDocument target, PdfDocument source, int sourcePageIndex, Transform transform, StampPlacement placement = StampPlacement.Overlay)
```

Stamps a source page onto every page of the target document.

### `RemoveStamp`

__static__

```csharp
static void RemoveStamp(Stream output, PdfDocument target, int targetPageIndex, string stampName)
```

Removes a previously placed stamp from a target page by its resource name (for example "CvStamp0"), dropping both the stamp's content-stream invocation and its XObject resource entry. Original page content and any other stamps are preserved. The now-orphaned form object is left in the file; it is collected if the document is later optimized.

### `ReplaceStamp`

__static__

```csharp
static void ReplaceStamp(Stream output, PdfDocument target, int targetPageIndex, string stampName, PdfDocument source, int sourcePageIndex, Transform transform, StampPlacement placement = StampPlacement.Overlay)
```

Replaces a stamp on a target page in a single pass: removes the stamp named `stampName` and places `source` in its stead. The replacement receives a fresh, non-colliding name.

### `Place`

__static__

```csharp
static void Place(Stream output, PdfDocument target, IReadOnlyList<int> targetPageIndices, PdfDocument source, int sourcePageIndex, Transform transform, StampPlacement placement = StampPlacement.Overlay)
```

Stamps a source page onto a set of target pages.

### `PlaceMany`

__static__

```csharp
static void PlaceMany(Stream output, PdfDocument target, IReadOnlyList<StampRequest> stamps)
```

Places any number of stamps — on any mix of target pages, from any mix of source documents and pages, each with its own transform, placement and opacity — in a single pass, writing the document once. Stamps on the same page are drawn in list order (later stamps paint over earlier ones within the same `StampPlacement`). Each distinct source page is imported once as a form XObject and shared across every stamp that uses it.

**Parameters**

- `output` — The stream to write the stamped PDF to.
- `target` — The document to stamp.
- `stamps` — The stamps to place. <exception cref="ArgumentNullException"> Thrown when an argument or any stamp's source is null. </exception> <exception cref="ArgumentOutOfRangeException"> Thrown when a stamp's target page index is outside the document, its source page index is outside the source document, or its opacity is outside [0, 1]. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/PageStamper.cs`](../../../src/Chuvadi.Pdf.Operations/PageStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
