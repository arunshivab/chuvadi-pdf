# PdfSignatureVerifyExtensions

**Class** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

The user-visible `Verify()` entry point on `PdfSignature`.

```csharp
public static class PdfSignatureVerifyExtensions
```

## Methods

### `Verify`

__static__

```csharp
static SignatureVerificationResult Verify(this PdfSignature signature, PdfDocument document, SignatureVerifyOptions? options = null)
```

Verifies `signature` against the bytes it covers in `document`.

**Parameters**

- `signature` — The signature to verify.
- `document` — The PDF the signature came from.
- `options` — Optional verification settings.

**Returns:** A `SignatureVerificationResult` describing the outcome.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifyExtensions.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifyExtensions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
