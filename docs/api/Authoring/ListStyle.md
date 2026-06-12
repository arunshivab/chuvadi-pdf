# ListStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

List styling for bulleted and numbered lists.

```csharp
public sealed class ListStyle
```

## Properties

### `Default`

__static__

```csharp
static ListStyle Default
```

Default list style: 11-point Helvetica, "•" bullets, 18-point indent.

### `Font`

```csharp
ReportFont Font
```

Gets or initialises the font. Default: regular Helvetica.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the font size in points. Default: 11.

### `Color`

```csharp
Color Color
```

Gets or initialises the text colour. Default: black.

### `Bullet`

```csharp
string Bullet
```

Gets or initialises the bullet marker for unordered lists. Default: "•".

### `Numbering`

```csharp
NumberingFormat Numbering
```

Gets or initialises the numbering scheme for ordered lists. Default: Arabic.

### `NumberSuffix`

```csharp
string NumberSuffix
```

Gets or initialises the suffix appended after an ordered-list number. Default: ".".

### `StartAt`

```csharp
int StartAt
```

Gets or initialises the first number of an ordered list. Default: 1.

### `MarkerIndent`

```csharp
double MarkerIndent
```

Gets or initialises the indent, in points, from the column edge to the marker. Default: 6.

### `TextIndent`

```csharp
double TextIndent
```

Gets or initialises the indent, in points, from the column edge to the item text. Default: 24.

### `LineSpacing`

```csharp
double LineSpacing
```

Gets or initialises the line spacing as a multiple of the font size. Default: 1.25.

### `ItemSpacing`

```csharp
double ItemSpacing
```

Gets or initialises the vertical space, in points, between list items. Default: 2.

### `SpaceAfter`

```csharp
double SpaceAfter
```

Gets or initialises the vertical space, in points, after the whole list. Default: 6.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
