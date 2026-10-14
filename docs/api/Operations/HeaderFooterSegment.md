# HeaderFooterSegment

**Class** in `Chuvadi.Pdf.Operations` (Operations)

One header/footer segment exactly as `HeaderFooter.Apply` draws it on a page: token-substituted text plus placement in PDF page coordinates (origin bottom-left, points). Produced by `HeaderFooter.ResolveSegments`.

```csharp
public sealed class HeaderFooterSegment
```

## Properties

### `Band`

```csharp
HeaderFooterBand Band
```

Gets the band (header or footer).

### `Alignment`

```csharp
HeaderFooterAlignment Alignment
```

Gets the alignment slot (left, centre, right).

### `Text`

```csharp
string Text
```

Gets the fully token-substituted text.

### `X`

```csharp
double X
```

Gets the x of the text origin, in page points.

### `BaselineY`

```csharp
double BaselineY
```

Gets the baseline y, in page points (origin bottom-left).

### `Width`

```csharp
double Width
```

Gets the measured advance width of the text, in points.

### `FontSize`

```csharp
double FontSize
```

Gets the font size, in points.

---

_Source: [`src/Chuvadi.Pdf.Operations/HeaderFooterSegment.cs`](../../../src/Chuvadi.Pdf.Operations/HeaderFooterSegment.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
