# ReadingOrderSource

**Enum** in `Chuvadi.Pdf.Text` (Text)

Where a page's reading order comes from.

```csharp
public enum ReadingOrderSource
```

## Values

| Name | Description |
|---|---|
| `Rows` | Lines from the top of the page to the bottom, each read along its baseline (as in 3.28). |
| `Columns` | Bands from the top of the page to the bottom; inside a band with more than one column, each column is read before the next. |
| `Tagged` | The order of the document's structure tree (a Tagged PDF). |

---

_Source: [`src/Chuvadi.Pdf.Text/PageLayout.cs`](../../../src/Chuvadi.Pdf.Text/PageLayout.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
