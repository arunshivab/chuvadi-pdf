# ImagePdfOptions

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Options for `ImagePdfConverter`.

```csharp
public sealed class ImagePdfOptions
```

## Properties

### `Default`

__static__

```csharp
static ImagePdfOptions Default
```

Default options: page sized to the image at 96 DPI.

### `Sizing`

```csharp
ImagePageSizing Sizing
```

Gets or initialises the page sizing strategy. Default: `ImagePageSizing.SizeToImage`.

### `Dpi`

```csharp
double Dpi
```

Gets or initialises the resolution, in pixels per inch, used to convert image pixels to page points. Default: 96.

### `PageSize`

```csharp
PageSize PageSize
```

Gets or initialises the paper size used by `ImagePageSizing.FitToPage`. Default: A4. Use `PageSize.Landscape` for landscape orientation.

### `Margin`

```csharp
double Margin
```

Gets or initialises the page margin in points, applied on all four sides under `ImagePageSizing.FitToPage`. Default: 36 (half an inch).

### `CenterOnPage`

```csharp
bool CenterOnPage
```

Gets or initialises whether the image is centred inside the content area under `ImagePageSizing.FitToPage`; when false the image is placed at the top-left margin corner. Default: true.

### `UpscaleSmallImages`

```csharp
bool UpscaleSmallImages
```

Gets or initialises whether an image smaller than the content area is scaled up to fill it under `ImagePageSizing.FitToPage`. When false (the default) small images render at their natural `Dpi`-derived size.

### `ExpandTiffFrames`

```csharp
bool ExpandTiffFrames
```

Gets or initialises whether a multi-frame TIFF expands to one PDF page per frame. When false only the first frame converts. Default: true.

### `Title`

```csharp
string? Title
```

Gets or initialises the document's /Title metadata.

### `Author`

```csharp
string? Author
```

Gets or initialises the document's /Author metadata.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs`](../../../src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
