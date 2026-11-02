# ExtractionStrategy

**Enum** in `Chuvadi.Pdf.Text` (Text)

Specifies the text extraction strategy.

```csharp
public enum ExtractionStrategy
```

## Values

| Name | Description |
|---|---|
| `Operator` | Stream-order extraction. Fastest. Correct for most born-digital PDFs. |
| `Layout` | Layout-aware extraction. Groups by line, sorts by X position. Better for multi-column and table-heavy PDFs. Since 3.29 it follows a Tagged PDF's structure tree when the tree orders the page's text, and otherwise reads columns of running text one after another (`TextExtractor.AnalyzeLayout(PdfPage)`). |

---

_Source: [`src/Chuvadi.Pdf.Text/TextExtractor.cs`](../../../src/Chuvadi.Pdf.Text/TextExtractor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
