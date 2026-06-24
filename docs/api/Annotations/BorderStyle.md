# BorderStyle

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Border style for an annotation, describing width, style, and (for dashed borders) dash pattern. PDF 32000-1:2008 §12.5.4 — Border styles.

```csharp
public sealed class BorderStyle
```

## Properties

### `Width`

```csharp
float Width
```

Gets the border width in PDF user-space units.

### `Style`

```csharp
BorderStyleType Style
```

Gets the border style kind.

### `DashPattern`

```csharp
IReadOnlyList<float>? DashPattern
```

Gets the dash pattern. Each entry alternates between on-length and off-length. Null means use the default (3 units on, 3 off) when `Style` is `BorderStyleType.Dashed`, and is ignored otherwise.

---

_Source: [`src/Chuvadi.Pdf.Annotations/BorderStyle.cs`](../../../src/Chuvadi.Pdf.Annotations/BorderStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
