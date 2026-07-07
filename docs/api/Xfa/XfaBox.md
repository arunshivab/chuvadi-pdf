# XfaBox

**Class** in `Chuvadi.Pdf.Xfa.Layout` (Xfa)

A single positioned box produced by the layout engine, expressed in device space (PDF points, origin at the page's top-left, y increasing downward — matching the authoring layer's top-left drawing API).

```csharp
public sealed class XfaBox
```

## Constructors

### `XfaBox(double x, double y, double width, double height)`

Initializes a box at the given device-space rectangle.

**Parameters**

- `x` — Left edge in points from the page left.
- `y` — Top edge in points from the page top.
- `width` — Box width in points.
- `height` — Box height in points.

## Properties

### `X`

```csharp
double X
```

Gets the left edge in points from the page left.

### `Y`

```csharp
double Y
```

Gets the top edge in points from the page top.

### `Width`

```csharp
double Width
```

Gets the box width in points.

### `Height`

```csharp
double Height
```

Gets the box height in points.

### `Right`

```csharp
double Right => X + Width
```

Gets the right edge (X + Width) in points.

### `Bottom`

```csharp
double Bottom => Y + Height
```

Gets the bottom edge (Y + Height) in points.

### `Text`

```csharp
string? Text
```

Gets or sets the text content to render in this box, if any.

### `Font`

```csharp
XfaFont? Font
```

Gets or sets the font applied to `Text`, if any.

### `HAlign`

```csharp
XfaHAlign HAlign
```

Gets or sets the horizontal alignment of the text.

### `VAlign`

```csharp
XfaVAlign VAlign
```

Gets or sets the vertical alignment of the text.

### `Border`

```csharp
XfaBorder? Border
```

Gets or sets the border to stroke and/or fill, if any.

### `Widget`

```csharp
XfaUiKind? Widget
```

Gets or sets the kind of widget this box represents, when it is a field.

### `WidgetChecked`

```csharp
bool? WidgetChecked
```

Gets or sets a value indicating whether the widget is in the "on" state (used by check buttons and radios). Null for non-toggle widgets.

### `WidgetRound`

```csharp
bool WidgetRound
```

Gets or sets a value indicating whether a check-button widget renders as a radio button (round mark) rather than a square check box.

### `ImageBytes`

```csharp
System.ReadOnlyMemory<byte>? ImageBytes
```

Gets or sets the decoded image payload for image fields. Null when the field carries no image data.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Layout/XfaBox.cs`](../../../src/Chuvadi.Pdf.Xfa/Layout/XfaBox.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
