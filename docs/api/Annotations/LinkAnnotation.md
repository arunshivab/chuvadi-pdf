# LinkAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Hyperlink annotation (§12.5.6.5). Targets either a URI or a page in the same document.

```csharp
public sealed class LinkAnnotation : PdfAnnotation
```

## Constructors

### `LinkAnnotation(int pageIndex, RectangleF rect, Uri uri, string? contents = null)`

Initialises a link to an external URI.

### `LinkAnnotation(int pageIndex, RectangleF rect, int destinationPageIndex, string? contents = null)`

Initialises a link to a destination page in the same document.

## Properties

### `Uri`

```csharp
Uri? Uri
```

Gets the external URI target, or null when the link is internal.

### `DestinationPageIndex`

```csharp
int DestinationPageIndex
```

Gets the zero-based destination page index for an internal link, or -1 when the link is external.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
