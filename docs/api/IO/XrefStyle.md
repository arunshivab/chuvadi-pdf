# XrefStyle

**Enum** in `Chuvadi.Pdf.IO` (IO)

Selects the cross-reference format `PdfWriter` writes.

```csharp
public enum XrefStyle
```

## Remarks

Both styles produce valid PDFs. `Classic` maximises reader compatibility; `Stream` produces smaller files by packing objects into object streams and replacing the plaintext cross-reference table with a compressed cross-reference stream.

## Values

| Name | Description |
|---|---|
| `Classic` | Classic 20-byte cross-reference table with a plaintext trailer (PDF 1.4+). Every object is written as a direct indirect object. Maximum reader compatibility. This is the default. PDF 32000-1:2008 §7.5.4. |
| `Stream` | Object streams plus a cross-reference stream (PDF 1.5+). Eligible objects are packed into compressed object streams and the cross-reference table is itself written as a compressed stream, producing a smaller file. PDF 32000-1:2008 §7.5.7 and §7.5.8. |

---

_Source: [`src/Chuvadi.Pdf.IO/XrefStyle.cs`](../../../src/Chuvadi.Pdf.IO/XrefStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
