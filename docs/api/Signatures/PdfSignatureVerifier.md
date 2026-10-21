# PdfSignatureVerifier

**Class** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

Orchestrates verification of a single `PdfSignature`.

```csharp
public static class PdfSignatureVerifier
```

## Methods

### `Verify`

__static__

```csharp
static SignatureVerificationResult Verify(PdfSignature signature, PdfDocument document, SignatureVerifyOptions? options = null)
```

Verifies `signature` against the bytes it covers in `document`.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifier.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
