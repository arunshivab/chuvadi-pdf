# ShapingFeatures

**Class** in `Chuvadi.Pdf.Text.Shaping` (Text)

Controls which OpenType features `TextShaper` applies when shaping a run. Features absent from the font are silently ignored.

```csharp
public sealed class ShapingFeatures
```

## Properties

### `Default`

__static__

```csharp
static ShapingFeatures Default
```

Gets the default feature set: ccmp, locl, calt, liga, kern, mark, mkmk enabled; all optional features off.

### `Ccmp`

```csharp
bool Ccmp
```

Gets or inits whether glyph composition/decomposition (ccmp) is enabled. Default: true.

### `Locl`

```csharp
bool Locl
```

Gets or inits whether localised forms (locl) are enabled. Default: true.

### `Calt`

```csharp
bool Calt
```

Gets or inits whether contextual alternates (calt) are enabled. Default: true.

### `Liga`

```csharp
bool Liga
```

Gets or inits whether standard ligatures (liga) are enabled. Default: true.

### `Cpsp`

```csharp
bool Cpsp
```

Gets or inits whether capital spacing (cpsp) is enabled. Default: true.

### `Kern`

```csharp
bool Kern
```

Gets or inits whether kerning (kern) is enabled. Default: true.

### `Mark`

```csharp
bool Mark
```

Gets or inits whether mark-to-base attachment (mark) is enabled. Default: true.

### `Mkmk`

```csharp
bool Mkmk
```

Gets or inits whether mark-to-mark attachment (mkmk) is enabled. Default: true.

### `Ordn`

```csharp
bool Ordn
```

Gets or inits whether ordinals (ordn) are enabled. Default: false.

### `Frac`

```csharp
bool Frac
```

Gets or inits whether fractions (frac) are enabled. Default: false.

### `Numr`

```csharp
bool Numr
```

Gets or inits whether numerator (numr) forms are enabled. Default: false.

### `Dnom`

```csharp
bool Dnom
```

Gets or inits whether denominator (dnom) forms are enabled. Default: false.

### `Sups`

```csharp
bool Sups
```

Gets or inits whether superscript (sups) forms are enabled. Default: false.

### `Subs`

```csharp
bool Subs
```

Gets or inits whether subscript (subs) forms are enabled. Default: false.

### `Sinf`

```csharp
bool Sinf
```

Gets or inits whether scientific inferiors (sinf) are enabled. Default: false.

### `Case`

```csharp
bool Case
```

Gets or inits whether case-sensitive forms (case) are enabled. Default: false.

### `Zero`

```csharp
bool Zero
```

Gets or inits whether slashed zero (zero) is enabled. Default: false.

### `Dlig`

```csharp
bool Dlig
```

Gets or inits whether discretionary ligatures (dlig) are enabled. Default: false.

### `Pnum`

```csharp
bool Pnum
```

Gets or inits whether proportional numbers (pnum) are enabled. Default: false.

### `Tnum`

```csharp
bool Tnum
```

Gets or inits whether tabular numbers (tnum) are enabled. Default: false.

### `Salt`

```csharp
bool Salt
```

Gets or inits whether salt alternates (salt) are enabled. Default: false.

### `Aalt`

```csharp
bool Aalt
```

Gets or inits whether all-alternates (aalt) are enabled. Default: false.

### `Ss01`

```csharp
bool Ss01
```

Gets or inits stylistic set 01 (ss01). Default: false.

### `Ss02`

```csharp
bool Ss02
```

Gets or inits stylistic set 02 (ss02). Default: false.

### `Ss03`

```csharp
bool Ss03
```

Gets or inits stylistic set 03 (ss03). Default: false.

### `Ss04`

```csharp
bool Ss04
```

Gets or inits stylistic set 04 (ss04). Default: false.

### `Ss05`

```csharp
bool Ss05
```

Gets or inits stylistic set 05 (ss05). Default: false.

### `Ss06`

```csharp
bool Ss06
```

Gets or inits stylistic set 06 (ss06). Default: false.

### `Ss07`

```csharp
bool Ss07
```

Gets or inits stylistic set 07 (ss07). Default: false.

### `Ss08`

```csharp
bool Ss08
```

Gets or inits stylistic set 08 (ss08). Default: false.

### `Cv01`

```csharp
bool Cv01
```

Gets or inits character variant 01 (cv01). Default: false.

### `Cv02`

```csharp
bool Cv02
```

Gets or inits character variant 02 (cv02). Default: false.

### `Cv03`

```csharp
bool Cv03
```

Gets or inits character variant 03 (cv03). Default: false.

### `Cv04`

```csharp
bool Cv04
```

Gets or inits character variant 04 (cv04). Default: false.

### `Cv05`

```csharp
bool Cv05
```

Gets or inits character variant 05 (cv05). Default: false.

### `Cv06`

```csharp
bool Cv06
```

Gets or inits character variant 06 (cv06). Default: false.

### `Cv07`

```csharp
bool Cv07
```

Gets or inits character variant 07 (cv07). Default: false.

### `Cv08`

```csharp
bool Cv08
```

Gets or inits character variant 08 (cv08). Default: false.

### `Cv09`

```csharp
bool Cv09
```

Gets or inits character variant 09 (cv09). Default: false.

### `Cv10`

```csharp
bool Cv10
```

Gets or inits character variant 10 (cv10). Default: false.

### `Cv11`

```csharp
bool Cv11
```

Gets or inits character variant 11 (cv11). Default: false.

### `Cv12`

```csharp
bool Cv12
```

Gets or inits character variant 12 (cv12). Default: false.

### `Cv13`

```csharp
bool Cv13
```

Gets or inits character variant 13 (cv13). Default: false.

### `Cv14`

```csharp
bool Cv14
```

Gets or inits character variant 14 (cv14). Default: false.

---

_Source: [`src/Chuvadi.Pdf.Text.Shaping/ShapingFeatures.cs`](../../../src/Chuvadi.Pdf.Text.Shaping/ShapingFeatures.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
