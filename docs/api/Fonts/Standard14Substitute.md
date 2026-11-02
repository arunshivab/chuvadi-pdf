# Standard14Substitute

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Chooses a standard 14 font to draw text whose font the PDF names but does not embed ("Arial", "TimesNewRomanPSMT", "Calibri,Bold").

```csharp
public static class Standard14Substitute
```

## Remarks

The standard 14 outlines are Liberation Sans, Liberation Serif and Liberation Mono (metric-compatible with Arial and Helvetica, Times New Roman and Times, and Courier New and Courier), plus URW symbol fonts for Symbol and ZapfDingbats. The family comes from the font's name, then from its descriptor flags (fixed pitch, serif); bold and italic from the name, the flags, `/FontWeight` and `/ItalicAngle`.

## Methods

### `Choose`

__static__

```csharp
static string? Choose(string baseFont, int flags, double italicAngle, double fontWeight)
```

Returns the standard 14 font that best stands in for a font that is not embedded, or null when none can (a symbol font such as Wingdings, whose pictures no standard 14 font has).

**Parameters**

- `baseFont` — The font's `/BaseFont` name; a subset prefix ("ABCDEF+") is ignored.
- `flags` — The font descriptor's `/Flags`, or 0.
- `italicAngle` — The descriptor's `/ItalicAngle`, or 0.
- `fontWeight` — The descriptor's `/FontWeight`, or 0 when absent.

**Returns:** A standard 14 font name, or null. <exception cref="ArgumentNullException">`baseFont` is null.</exception>

### `OutlineSource`

__static__

```csharp
static string OutlineSource(string standard14Name)
```

Returns the name of the typeface whose outlines draw the given standard 14 font ("Liberation Sans" for Helvetica), for messages.

**Parameters**

- `standard14Name` — A standard 14 font name.

**Returns:** The typeface name. <exception cref="ArgumentNullException">`standard14Name` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Standard14Substitute.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Standard14Substitute.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
