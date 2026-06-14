# FontStyleClassifier

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Derives a `FontStyle` from a font's base name and, when available, its FontDescriptor `/Flags`, `/ItalicAngle`, and `/StemV`. Used by both the display-list text path and the SVG renderer so style classification stays consistent across consumers. Name heuristics and descriptor signals are combined — either source alone is sufficient to mark a run bold or italic.

```csharp
public static class FontStyleClassifier
```

## Methods

### `Classify`

__static__

```csharp
static FontStyle Classify(string baseFont, int? flags, double? italicAngle, int? stemV)
```

Classifies a font into a `FontStyle`.

**Parameters**

- `baseFont` — Base font name (subset tag tolerated).
- `flags` — FontDescriptor `/Flags`, if known.
- `italicAngle` — FontDescriptor `/ItalicAngle`, if known.
- `stemV` — FontDescriptor `/StemV`, if known.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/FontStyleClassifier.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/FontStyleClassifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
