# PdfAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Base class for all modelled annotations. Carries the fields shared by every annotation subtype per PDF 32000-1:2008 §12.5.2.

```csharp
public abstract class PdfAnnotation
```

## Properties

### `Type`

```csharp
AnnotationType Type
```

Gets the annotation subtype.

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based page index the annotation lives on.

### `Rect`

```csharp
RectangleF Rect
```

Gets the rectangle on the page in PDF user space.

### `Contents`

```csharp
string? Contents
```

Gets the contents string (the text for Text and FreeText annotations; the alternative description for graphical annotations).

### `Color`

```csharp
ColorF? Color
```

Gets the annotation colour, or null for the viewer default.

### `Author`

```csharp
string? Author
```

Gets the annotation author / title (PDF /T).

### `Opacity`

```csharp
float Opacity
```

Gets the opacity 0..1 (PDF /CA). Default 1.0.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
