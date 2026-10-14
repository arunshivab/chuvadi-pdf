# RedactionSurvivor

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

One piece of content found inside a region.

```csharp
public sealed class RedactionSurvivor
```

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based page index.

### `Kind`

```csharp
RedactionSurvivorKind Kind
```

Gets what kind of content this is.

### `Bounds`

```csharp
RectangleF Bounds
```

Gets the content's bounding box in page space (points, origin bottom-left).

### `Text`

```csharp
string? Text
```

Gets the text: the glyph's Unicode for `RedactionSurvivorKind.Text`, the annotation's `/Contents` for `RedactionSurvivorKind.Annotation`, otherwise null.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
