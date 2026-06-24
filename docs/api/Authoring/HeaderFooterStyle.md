# HeaderFooterStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Header / footer band styling. The text may contain the tokens `{page}`, `{total}`, `{title}`, and `{date}`, replaced per page at save time; page numbers honour `PageNumbering`.

```csharp
public sealed class HeaderFooterStyle
```

## Properties

### `Text`

```csharp
string Text
```

Gets or initialises the band text (with optional tokens). Default: empty.

### `Font`

```csharp
ReportFont Font
```

Gets or initialises the font. Default: regular Helvetica.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the font size in points. Default: 9.

### `Color`

```csharp
Color Color
```

Gets or initialises the text colour. Default: mid gray.

### `Alignment`

```csharp
TextAlignment Alignment
```

Gets or initialises the horizontal alignment within the content width. Default: centre.

### `PageNumbering`

```csharp
NumberingFormat PageNumbering
```

Gets or initialises the numbering scheme applied to {page} and {total}. Default: Arabic.

### `ShowOnFirstPage`

```csharp
bool ShowOnFirstPage
```

Gets or initialises whether the band also draws on page 1. Default: true.

### `EdgeOffset`

```csharp
double EdgeOffset
```

Gets or initialises the distance, in points, from the page edge (top edge for headers, bottom edge for footers) to the band. Default: 25.

### `RuleLine`

```csharp
bool RuleLine
```

Gets or initialises whether a thin rule line separates the band from the content. Default: false.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
