# XfaNode

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

Base type for every parsed XFA template node. Carries the element name, the optional template `name` attribute, the child nodes, and the geometry and presence properties common to layout containers and leaves.

```csharp
public abstract class XfaNode
```

## Properties

### `ElementName`

```csharp
abstract string ElementName
```

Gets the XFA element name (for example "subform", "field").

### `Name`

```csharp
string? Name
```

Gets or sets the template `name` attribute, if present.

### `X`

```csharp
XfaMeasurement X
```

Gets or sets the explicit x offset within the parent container.

### `Y`

```csharp
XfaMeasurement Y
```

Gets or sets the explicit y offset within the parent container.

### `Width`

```csharp
XfaMeasurement? Width
```

Gets or sets the declared width, when specified.

### `Height`

```csharp
XfaMeasurement? Height
```

Gets or sets the declared height, when specified.

### `Presence`

```csharp
XfaPresence Presence
```

Gets or sets the presence (visibility / layout participation).

### `Margin`

```csharp
XfaMargin? Margin
```

Gets or sets the margin box, when specified.

### `Border`

```csharp
XfaBorder? Border
```

Gets or sets the border, when specified.

### `Children`

```csharp
IReadOnlyList<XfaNode> Children => _children
```

Gets the child nodes in document order.

## Methods

### `AddChild`

```csharp
void AddChild(XfaNode child) => _children.Add(child)
```

Appends a child node.

**Parameters**

- `child` — The child to append.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaNode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
