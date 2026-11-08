# BlackAndWhiteFills

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

How `PdfCompressionLevel.BlackAndWhite` and `PdfCompressionLevel.BlackAndWhiteWithColor` draw large grey fills (3.32.0). Lines and text are the same in both: every pixel that is not near-white becomes black, so thin grey text stays readable.

```csharp
public enum BlackAndWhiteFills
```

## Values

| Name | Description |
|---|---|
| `DotPattern` | Large fills become a dot pattern, so they stay visible as fills and the marks drawn over them stay readable. The default. Slightly larger. |
| `Solid` | Large fills become solid black. Slightly smaller. |

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
