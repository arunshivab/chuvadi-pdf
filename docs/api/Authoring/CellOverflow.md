# CellOverflow

**Enum** in `Chuvadi.Pdf.Authoring` (Authoring)

How cell text that exceeds the cell width is handled.

```csharp
public enum CellOverflow
```

## Values

| Name | Description |
|---|---|
| `Wrap` | Wrap onto additional lines; the row grows to fit (auto-height rows). |
| `Truncate` | Cut the text at the cell edge. |
| `Ellipsis` | Cut the text and append an ellipsis. |

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
