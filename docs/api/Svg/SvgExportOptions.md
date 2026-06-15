# SvgExportOptions

**Class** in `Chuvadi.Pdf.Svg` (Svg)

Options for PDF → SVG export.

```csharp
public sealed class SvgExportOptions
```

## Properties

### `InlineImages`

```csharp
bool InlineImages
```

Embed images as base64 data URLs (default: true).

### `TextStrategy`

```csharp
SvgTextStrategy TextStrategy
```

Text rendering strategy. Defaults to `SvgTextStrategy.Selectable`.

### `FontStrategy`

```csharp
SvgFontStrategy FontStrategy
```

Font embedding strategy. Defaults to `SvgFontStrategy.EmbedAsWebFont`.

### `Precision`

```csharp
int Precision
```

Number of decimal places for emitted coordinates. Default 4.

### `PrettyPrint`

```csharp
bool PrettyPrint
```

Indent the SVG output (default: false, compact).

### `Background`

```csharp
ColorF? Background
```

Opaque colour for the page sheet, emitted as a full-page background rectangle behind all content. Defaults to `ColorF.White`, matching the rasteriser's default white paper. Set to `null` for a transparent SVG (useful when compositing over another background).

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExportOptions.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExportOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
