# XfaLayoutEngine

**Class** in `Chuvadi.Pdf.Xfa.Layout` (Xfa)

Resolves an XFA model subtree into a flat list of positioned `XfaBox`es in device space. Phase B handles `XfaLayout.Position` containers: each child is placed by its explicit x/y offset relative to the accumulated parent origin.

```csharp
public static class XfaLayoutEngine
```

## Methods

### `Layout`

__static__

```csharp
static IReadOnlyList<XfaBox> Layout(XfaNode root, double originX, double originY)
```

Lays out a model subtree starting at the given device-space origin.

**Parameters**

- `root` — The root node to lay out (typically the body subform).
- `originX` — The device-space x origin in points.
- `originY` — The device-space y origin in points.

**Returns:** The positioned boxes in document order.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Layout/XfaLayoutEngine.cs`](../../../src/Chuvadi.Pdf.Xfa/Layout/XfaLayoutEngine.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
