# XfaCaption

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A field caption: its text and placement relative to the value.

```csharp
public sealed class XfaCaption : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "caption"
```

<inheritdoc />

### `Text`

```csharp
string? Text
```

Gets or sets the caption text.

### `Placement`

```csharp
XfaCaptionPlacement Placement
```

Gets or sets the caption placement relative to the value area.

### `Reserve`

```csharp
XfaMeasurement? Reserve
```

Gets or sets the reserved size of the caption area, when specified.

### `Font`

```csharp
XfaFont? Font
```

Gets or sets the font applied to the caption text.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
