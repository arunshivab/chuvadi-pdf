# XfaField

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

An interactive field with an optional caption, value, and UI widget.

```csharp
public sealed class XfaField : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "field"
```

<inheritdoc />

### `Caption`

```csharp
XfaCaption? Caption
```

Gets or sets the field caption, when present.

### `Value`

```csharp
XfaValue? Value
```

Gets or sets the field value, when present.

### `Ui`

```csharp
XfaUi? Ui
```

Gets or sets the field UI widget descriptor, when present.

### `Font`

```csharp
XfaFont? Font
```

Gets or sets the font applied to the field value text.

### `HAlign`

```csharp
XfaHAlign HAlign
```

Gets or sets the horizontal alignment of value content.

### `VAlign`

```csharp
XfaVAlign VAlign
```

Gets or sets the vertical alignment of value content.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
