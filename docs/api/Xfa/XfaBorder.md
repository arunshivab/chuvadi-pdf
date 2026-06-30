# XfaBorder

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A node border: edge stroke and optional fill.

```csharp
public sealed class XfaBorder : XfaNode
```

## Properties

### `ElementName`

```csharp
override string ElementName => "border"
```

<inheritdoc />

### `EdgeThickness`

```csharp
XfaMeasurement EdgeThickness
```

Gets or sets the stroke width of the border edges.

### `EdgeColor`

```csharp
string? EdgeColor
```

Gets or sets the edge colour as an "r,g,b" triple (0-255), when specified.

### `FillColor`

```csharp
string? FillColor
```

Gets or sets the fill colour as an "r,g,b" triple (0-255), when specified.

### `HasEdge`

```csharp
bool HasEdge
```

Gets or sets a value indicating whether the border edges are visible.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
