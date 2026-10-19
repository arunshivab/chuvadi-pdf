# PdfFractionFormat

**Enum** in `Chuvadi.Pdf.Documents` (Documents)

How a number format displays fractional values (Table 261 `/F`).

```csharp
public enum PdfFractionFormat
```

## Values

| Name | Description |
|---|---|
| `Decimal` | Decimal, with the precision given by `PdfNumberFormat.Denominator`. |
| `Fraction` | A fraction whose denominator is `PdfNumberFormat.Denominator`. |
| `Round` | Rounded to the nearest whole unit. |
| `Truncate` | Truncated to a whole unit. |

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfMeasure.cs`](../../../src/Chuvadi.Pdf.Documents/PdfMeasure.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
