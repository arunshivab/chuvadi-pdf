# TableBorderMode

**Enum** in `Chuvadi.Pdf.Authoring` (Authoring)

Which grid lines a table draws.

```csharp
public enum TableBorderMode
```

## Values

| Name | Description |
|---|---|
| `None` | No lines at all. |
| `Grid` | The full grid: outline plus every interior row and column line. |
| `Outline` | The outer rectangle only. |
| `HorizontalOnly` | Horizontal lines only (row separators plus top and bottom edges). |
| `HeaderUnderlineOnly` | A single line under the header row only. |

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
