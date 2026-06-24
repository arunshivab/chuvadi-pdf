# IccProfile

**Class** in `Chuvadi.Pdf.Color` (Color)

Parsed ICC color profile. Provides a `ToSrgb` method to convert source-space color tuples to sRGB.

```csharp
public sealed class IccProfile
```

## Remarks

Tag types supported in v1: `desc` (textDescriptionType / multiLocalizedUnicodeType), `XYZ` (XYZType), `curv` (curveType), and tag-based 8/16-bit LUTs (`mft1`/`mft2`) for A2B0 / B2A0. Modern lutAtoBType / mAB blocks are recognized but only their fallback paths are honored; full B-curve → matrix → M-curves → CLUT → A-curves pipeline is Phase 2.2.  

 For CMYK→sRGB the typical path is: input CMYK → A2B0 LUT → PCS (Lab or XYZ) → matrix → sRGB. When A2B0 is absent the profile is unusable and ToSrgb returns the pure-math fallback (same as Phase 2.0 ImageEncoder).

## Properties

### `ColorSpace`

```csharp
IccColorSpace ColorSpace
```

The color space declared by the profile header.

### `Channels`

```csharp
int Channels
```

Number of color channels in the input color space.

## Methods

### `Parse`

__static__

```csharp
static IccProfile Parse(byte[] data)
```

Parses an ICC profile from a byte buffer.

---

_Source: [`src/Chuvadi.Pdf.Color/IccProfile.cs`](../../../src/Chuvadi.Pdf.Color/IccProfile.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
