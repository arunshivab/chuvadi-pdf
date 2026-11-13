# PdfColor

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A color value with explicit source color space. Conversion to sRGB happens in the renderer, not in the display list.

```csharp
public readonly record struct PdfColor(PdfColorSpace Space, double C0, double C1, double C2, double C3)
```

## Properties

### `Black`

__static__

```csharp
static PdfColor Black
```

Pure black in DeviceGray.

### `White`

__static__

```csharp
static PdfColor White
```

Pure white in DeviceGray.

## Methods

### `Gray`

__static__

```csharp
static PdfColor Gray(double g) => new(PdfColorSpace.DeviceGray, g, 0, 0, 0)
```

Creates a DeviceGray color.

### `Rgb`

__static__

```csharp
static PdfColor Rgb(double r, double g, double b) => new(PdfColorSpace.DeviceRgb, r, g, b, 0)
```

Creates a DeviceRGB color.

### `Cmyk`

__static__

```csharp
static PdfColor Cmyk(double c, double m, double y, double k)
```

Creates a DeviceCMYK color.

### `ToSrgb`

```csharp
(double R, double G, double B) ToSrgb() => Space switch
```

Converts this color to sRGB (r, g, b) in [0, 1].

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PdfColor.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PdfColor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
