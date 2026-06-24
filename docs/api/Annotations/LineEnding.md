# LineEnding

**Enum** in `Chuvadi.Pdf.Annotations` (Annotations)

Line ending style for Line and PolyLine annotations. PDF 32000-1:2008 §12.5.6.7, Table 176 — /LE entry values. Names match the PDF spec.

```csharp
public enum LineEnding
```

## Values

| Name | Description |
|---|---|
| `None` | No specific ending. (PDF /LE = None, the default.) |
| `Square` | Filled square. (PDF /LE = Square.) |
| `Circle` | Filled circle. (PDF /LE = Circle.) |
| `Diamond` | Filled diamond. (PDF /LE = Diamond.) |
| `OpenArrow` | Open arrowhead pointing along the line. (PDF /LE = OpenArrow.) |
| `ClosedArrow` | Filled arrowhead pointing along the line. (PDF /LE = ClosedArrow.) |
| `Butt` | Short line perpendicular to the line ending. (PDF /LE = Butt.) |
| `ROpenArrow` | Open arrowhead pointing away from the line. (PDF /LE = ROpenArrow.) |
| `RClosedArrow` | Filled arrowhead pointing away from the line. (PDF /LE = RClosedArrow.) |
| `Slash` | Short line at 45° to the line ending. (PDF /LE = Slash.) |

---

_Source: [`src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs`](../../../src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
