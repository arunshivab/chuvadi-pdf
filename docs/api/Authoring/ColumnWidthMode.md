# ColumnWidthMode

**Enum** in `Chuvadi.Pdf.Authoring` (Authoring)

How a column's width is specified.

```csharp
public enum ColumnWidthMode
```

## Values

| Name | Description |
|---|---|
| `Fraction` | Width is a fraction (0..1] of the table width. |
| `Points` | Width is an absolute number of points. |
| `Auto` | Width is an equal share of the space left after fixed and fractional columns. |

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
