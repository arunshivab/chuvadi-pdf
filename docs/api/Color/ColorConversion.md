# ColorConversion

**Class** in `Chuvadi.Pdf.Color` (Color)

Static color-conversion helpers. Default path uses the pure-math CMYK→sRGB approximation; pass an `IccProfile` for ICC-accurate conversion.

```csharp
public static class ColorConversion
```

## Methods

### `CmykToSrgb`

__static__

```csharp
static void CmykToSrgb(ReadOnlySpan<byte> cmyk, Span<byte> rgb)
```

Converts an interleaved CMYK pixel buffer to interleaved RGB in-place.

### `CmykToSrgb`

__static__

```csharp
static void CmykToSrgb(IccProfile profile, ReadOnlySpan<byte> cmyk, Span<byte> rgb)
```

Converts an interleaved CMYK buffer to RGB using an ICC profile.

---

_Source: [`src/Chuvadi.Pdf.Color/ColorConversion.cs`](../../../src/Chuvadi.Pdf.Color/ColorConversion.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
