# CompressionSkipReason

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

Why `PdfCompressor.Compress` declined to rewrite a document.

```csharp
public enum CompressionSkipReason
```

## Values

| Name | Description |
|---|---|
| `None` | The document was rewritten; no skip occurred. |
| `Signed` | The document is digitally signed and `CompressionOptions.AllowSignedRewrite` was not set. |
| `Encrypted` | The document is encrypted and `CompressionOptions.AllowEncryptedRewrite` was not set. |

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
