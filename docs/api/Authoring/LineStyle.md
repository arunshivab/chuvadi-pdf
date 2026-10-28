# LineStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

How a line or outline is stroked: width, dash pattern, caps and joins. Pass it to the `PageBuilder` drawing overloads that take one.

```csharp
public sealed class LineStyle
```

## Properties

### `Width`

```csharp
double Width
```

Gets the line width in points. Default 1.

### `DashPattern`

```csharp
IReadOnlyList<double> DashPattern
```

Gets the dash pattern: alternating dash and gap lengths in points, as in PDF's `d` operator. Empty (the default) draws a solid line.

### `DashPhase`

```csharp
double DashPhase
```

Gets how far into the dash pattern the line starts, in points. Default 0.

### `Cap`

```csharp
LineCap Cap
```

Gets the line cap. Default `LineCap.Butt`.

### `Join`

```csharp
LineJoin Join
```

Gets the line join. Default `LineJoin.Miter`.

### `MiterLimit`

```csharp
double MiterLimit
```

Gets the miter limit (PDF default 10).

---

_Source: [`src/Chuvadi.Pdf.Authoring/LineStyle.cs`](../../../src/Chuvadi.Pdf.Authoring/LineStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
