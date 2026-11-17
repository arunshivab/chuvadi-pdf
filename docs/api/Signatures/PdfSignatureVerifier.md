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

**Remarks:** Since 3.35.0 (D-396) `SignatureVerificationResult.IsValid` requires all three: the signature is mathematically intact (`SignatureVerificationResult.IntegrityVerified`), nothing but later signatures or validation data was added after signing (`SignatureVerificationResult.ModificationsAfterSigning`), and the signer's certificate chains to an anchor in `SignatureVerifyOptions.TrustStore`. Without a trust store an intact, unmodified signature is `SignatureVerificationStatus.TrustUnknown`, not valid.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifier.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/PdfSignatureVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
