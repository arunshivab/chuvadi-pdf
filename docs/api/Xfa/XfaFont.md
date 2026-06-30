# XfaFont

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A font descriptor applied to caption, value, or draw text.

```csharp
public sealed class XfaFont : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "font"
```

<inheritdoc />

### `Typeface`

```csharp
string? Typeface
```

Gets or sets the typeface name.

### `Size`

```csharp
double Size
```

Gets or sets the font size in points.

### `Bold`

```csharp
bool Bold
```

Gets or sets a value indicating whether the font is bold.

### `Italic`

```csharp
bool Italic
```

Gets or sets a value indicating whether the font is italic.

### `Color`

```csharp
string? Color
```

Gets or sets the text colour as an "r,g,b" triple (0-255), when specified.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
