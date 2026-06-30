# XfaDraw

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

Static, non-interactive content such as boilerplate text or lines.

```csharp
public sealed class XfaDraw : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "draw"
```

<inheritdoc />

### `Value`

```csharp
XfaValue? Value
```

Gets or sets the static value (text content), when present.

### `Font`

```csharp
XfaFont? Font
```

Gets or sets the font applied to the drawn text.

### `HAlign`

```csharp
XfaHAlign HAlign
```

Gets or sets the horizontal alignment of content.

### `VAlign`

```csharp
XfaVAlign VAlign
```

Gets or sets the vertical alignment of content.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
