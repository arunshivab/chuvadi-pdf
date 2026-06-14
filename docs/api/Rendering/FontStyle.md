# FontStyle

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Resolved presentation style for a text run — family, weight, and slant — derived from a font's base name and FontDescriptor. Carried on `TextOp` and surfaced on `TextRun` so callers can reconstruct formatted text.

```csharp
public readonly record struct FontStyle(string FontFamily, int Weight, FontSlant Slant, double ItalicAngle)
```

## Properties

### `IsBold`

```csharp
bool IsBold => Weight >= 600
```

True when the weight is bold or heavier (>= 600).

### `IsItalic`

```csharp
bool IsItalic => Slant != FontSlant.Normal
```

True when the slant is italic or oblique.

## Methods

### `new`

__static__

```csharp
static FontStyle Default => new(string.Empty, 400, FontSlant.Normal, 0.0)
```

A neutral upright 400-weight style with no family.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/FontStyle.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/FontStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
