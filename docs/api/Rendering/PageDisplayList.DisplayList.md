# PageDisplayList

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A page's content as a neutral, ordered sequence of `RenderOp`s.

```csharp
public sealed class PageDisplayList : IReadOnlyList<RenderOp>
```

## Remarks

Built by `DisplayListBuilder.Build(PdfDocument, int, bool)`; consumed by output adapters such as `SvgRenderer`, `WpfRenderer`, or future software rasterizers.  

 Pure value-like type: same page bytes, same display list. No rendering side effects.  

 v2.1.2: also exposes the page's font dictionaries keyed by the resource name used in `TextOp.FontKey`. This allows downstream renderers that want to embed font programs (e.g. `SvgRenderer` emitting CSS `@font-face` rules with base64-encoded TrueType data URLs) to access the source font dictionaries without re-walking the page resources or holding a reference to the original `PdfDocument`.

## Constructors

### `PageDisplayList(IReadOnlyList<RenderOp> ops, double mediaWidth, double mediaHeight, int rotation)`

Initialises a display list with the given ops and page metadata, with no font dictionaries attached. Equivalent to passing an empty dictionary for the font registry overload.

### `PageDisplayList(IReadOnlyList<RenderOp> ops, double mediaWidth, double mediaHeight, int rotation, IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey)`

Initialises a display list with the given ops, page metadata, and the page's font dictionaries keyed by the resource-name used in `TextOp.FontKey` (e.g. `"F1"`, `"TT2"`).

### `PageDisplayList(IReadOnlyList<RenderOp> ops, double mediaWidth, double mediaHeight, int rotation, IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey, IReadOnlyList<RenderingDiagnostic> diagnostics)`

Initialises a display list with the given ops, page metadata, font dictionaries, and the diagnostics accumulated during build (graceful-degradation events such as a font that could not be resolved). New in v2.1.8.

### `PageDisplayList(IReadOnlyList<RenderOp> ops, double mediaWidth, double mediaHeight, int rotation, IReadOnlyDictionary<string, PdfDictionary> fontDictsByKey, IReadOnlyList<RenderingDiagnostic> diagnostics, IReadOnlyList<OptionalContentGroup> optionalContentGroups)`

Initialises a display list with the given ops, page metadata, font dictionaries, diagnostics, and the document's optional-content groups (layers) declared in /OCProperties/OCGs. New in v2.3. Individual ops reference these layers by name through `RenderOp.Layers`.

## Properties

### `MediaBox`

```csharp
PdfRectangle MediaBox
```

The page's MediaBox (3.31). The default (all zeros) when the list was not built from a page.

### `CropBox`

```csharp
PdfRectangle CropBox
```

The CropBox, the region a viewer shows and a printer prints (3.31), defaulting to the MediaBox. PDF 32000-1:2008 §14.11.2.

### `BleedBox`

```csharp
PdfRectangle BleedBox
```

The BleedBox (3.31), defaulting to the CropBox.

### `TrimBox`

```csharp
PdfRectangle TrimBox
```

The TrimBox (3.31), defaulting to the CropBox.

### `ArtBox`

```csharp
PdfRectangle ArtBox
```

The ArtBox (3.31), defaulting to the CropBox.

### `Fonts`

```csharp
IReadOnlyList<DisplayListFont> Fonts
```

Every font used by text on the page, once each, in first-use order (3.31). The same objects as `TextOp.Font`.

### `TransparencyGroups`

```csharp
IReadOnlyList<TransparencyGroup> TransparencyGroups
```

The page's transparency groups (3.31, D-173), by id: entry `i` has `TransparencyGroup.Id` `i + 1`. Empty when there are none.

### `HasTransparency`

```csharp
bool HasTransparency
```

True when anything on the page needs transparency (3.31): a blend mode other than Normal, a soft mask, an opacity below 1, or a transparency group.

### `MediaWidth`

```csharp
double MediaWidth
```

Page width in points.

### `MediaHeight`

```csharp
double MediaHeight
```

Page height in points.

### `Rotation`

```csharp
int Rotation
```

Clockwise rotation in degrees (0, 90, 180, 270).

### `FontDictsByKey`

```csharp
IReadOnlyDictionary<string, PdfDictionary> FontDictsByKey
```

Font dictionaries for every font referenced on this page, keyed by the resource-name used in `TextOp.FontKey`. Empty when the builder did not populate it (e.g. legacy callers using the four-argument constructor). Never null.

### `Diagnostics`

```csharp
IReadOnlyList<RenderingDiagnostic> Diagnostics
```

Graceful-degradation events recorded by the builder during page construction (e.g. a font that could not be resolved, causing `DiagnosticKind.DecodeFallback`). Empty when nothing went wrong. Never null. New in v2.1.8 — older callers using constructors without this argument see an empty list.

### `OptionalContentGroups`

```csharp
IReadOnlyList<OptionalContentGroup> OptionalContentGroups
```

The optional-content groups (layers) declared in the document's /OCProperties/OCGs array, in declaration order, with default visibility resolved. Empty when the document has no optional content or when a constructor without this argument was used. Never null. New in v2.3. See `RenderOp.Layers` for per-op membership.

### `Count`

```csharp
int Count => _ops.Count
```

<inheritdoc />

### `index]`

```csharp
RenderOp this[int index] => _ops[index]
```

<inheritdoc />

## Methods

### `GetEnumerator`

```csharp
IEnumerator<RenderOp> GetEnumerator() => _ops.GetEnumerator()
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PageDisplayList.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PageDisplayList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
