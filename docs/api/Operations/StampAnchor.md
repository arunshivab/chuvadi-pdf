# StampAnchor

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

The twelve positions a text stamp can be anchored to on a page: the three top and three bottom positions (horizontal text), plus three on each vertical edge (text rotated 90° to read up the left edge or down the right edge). Margins are measured inward from the page edge to the anchor.

```csharp
public enum StampAnchor
```

## Values

| Name | Description |
|---|---|
| `TopLeft` | Top-left, horizontal. |
| `TopCenter` | Top-centre, horizontal. |
| `TopRight` | Top-right, horizontal. |
| `BottomLeft` | Bottom-left, horizontal. |
| `BottomCenter` | Bottom-centre, horizontal. |
| `BottomRight` | Bottom-right, horizontal. |
| `LeftEdgeTop` | Left edge, top, text reading upward (rotated 90° CCW). |
| `LeftEdgeMiddle` | Left edge, middle, text reading upward (rotated 90° CCW). |
| `LeftEdgeBottom` | Left edge, bottom, text reading upward (rotated 90° CCW). |
| `RightEdgeTop` | Right edge, top, text reading downward (rotated 90° CW). |
| `RightEdgeMiddle` | Right edge, middle, text reading downward (rotated 90° CW). |
| `RightEdgeBottom` | Right edge, bottom, text reading downward (rotated 90° CW). |

---

_Source: [`src/Chuvadi.Pdf.Operations/StampAnchor.cs`](../../../src/Chuvadi.Pdf.Operations/StampAnchor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
