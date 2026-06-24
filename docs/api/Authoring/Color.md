# Color

**Record** in `Chuvadi.Pdf.Authoring` (Authoring)

An RGB color in [0, 1] floating-point space. Internally maps to PDF DeviceRGB.

```csharp
public readonly record struct Color(double R, double G, double B)
```

## Methods

### `FromBytes`

__static__

```csharp
static Color FromBytes(byte r, byte g, byte b)
```

Creates a color from 0–255 byte channels.

### `FromHex`

__static__

```csharp
static Color FromHex(string hex)
```

Creates a color from a hex string ("#RRGGBB" or "RRGGBB").

---

_Source: [`src/Chuvadi.Pdf.Authoring/Colors.cs`](../../../src/Chuvadi.Pdf.Authoring/Colors.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
