# StampRequest

**Class** in `Chuvadi.Pdf.Operations` (Operations)

One stamp for `PageStamper.PlaceMany`: a source page drawn onto a target page under an affine transform, over or under the existing content, at a constant opacity.

```csharp
public sealed class StampRequest
```

## Constructors

### `StampRequest(int targetPageIndex, PdfDocument source, int sourcePageIndex, Transform transform)`

Initializes a stamp request.

**Parameters**

- `targetPageIndex` — Zero-based page of the target document to stamp.
- `source` — The document holding the stamp page.
- `sourcePageIndex` — Zero-based page of `source` to stamp.
- `transform` — Maps the source page's space onto the target page. <exception cref="ArgumentNullException">Thrown when `source` is null.</exception>

## Properties

### `TargetPageIndex`

```csharp
int TargetPageIndex
```

Gets the zero-based target page index.

### `Source`

```csharp
PdfDocument Source
```

Gets the source document.

### `SourcePageIndex`

```csharp
int SourcePageIndex
```

Gets the zero-based source page index.

### `Transform`

```csharp
Transform Transform
```

Gets the transform from source page space to target page space.

### `Placement`

```csharp
StampPlacement Placement
```

Gets whether the stamp is drawn over (default) or under the existing content.

### `Opacity`

```csharp
double Opacity
```

Gets the constant opacity applied to the whole stamp (fill and stroke), 0 fully transparent to 1 fully opaque (default). It multiplies with any transparency already inside the stamp, such as an image's soft mask.

---

_Source: [`src/Chuvadi.Pdf.Operations/StampRequest.cs`](../../../src/Chuvadi.Pdf.Operations/StampRequest.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
