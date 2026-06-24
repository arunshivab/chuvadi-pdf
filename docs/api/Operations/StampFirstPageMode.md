# StampFirstPageMode

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

Controls how the document's first page (page index 0) participates in a `StampNumbering` running sequence.

```csharp
public enum StampFirstPageMode
```

## Values

| Name | Description |
|---|---|
| `Number` | The first page is numbered and consumes the start value. |
| `SkipKeepCount` | The first page is not stamped but still reserves its place in the sequence, so the second page shows the start value plus one. |
| `SkipRenumber` | The first page is neither stamped nor counted, so the second page shows the start value. |

---

_Source: [`src/Chuvadi.Pdf.Operations/StampFirstPageMode.cs`](../../../src/Chuvadi.Pdf.Operations/StampFirstPageMode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
