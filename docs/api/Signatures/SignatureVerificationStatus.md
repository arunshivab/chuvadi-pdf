# SignatureVerificationStatus

**Enum** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

The overall outcome of verifying a PDF signature.

```csharp
public enum SignatureVerificationStatus
```

## Values

| Name | Description |
|---|---|
| `Valid` | The signature is cryptographically valid, nothing but later signatures or validation data was added after signing, and the signer's certificate chain validates to an anchor in the trust store. Since 3.35.0 a signature is never valid without a trust store (see `TrustUnknown`) or after other changes (see `ModifiedAfterSigning`). |
| `Invalid` | The cryptographic signature does not match. |
| `DigestMismatch` | The signature's message digest does not match the hash of the signed bytes. |
| `SignerCertificateNotFound` | The signer certificate could not be located inside the CMS envelope. |
| `UnsupportedSubFilter` | The /SubFilter is not CMS-based; Chuvadi does not know how to verify it. |
| `UnsupportedAlgorithm` | The signature uses an algorithm Chuvadi does not implement. |
| `MalformedSignature` | The signature container could not be parsed. |
| `TrustChainBroken` | The signature is cryptographically valid, but the signer's certificate does not chain to any trust anchor in the supplied trust store. |
| `TrustChainCertificateOutOfValidity` | The signature is cryptographically valid, but a certificate in the chain has expired or is not yet valid at the validation time. |
| `TrustChainInvalid` | The signature is cryptographically valid, but the signer's certificate chain failed RFC 5280 §6.1 path validation for a reason other than validity-period violation. |
| `TrustChainCertificateRevoked` | The signature is cryptographically valid and chains to a trust anchor, but a certificate in the chain is on a Certificate Revocation List. |
| `TrustUnknown` | The signature is intact and the file was not changed after signing, but no trust store was supplied, so who signed it is not verified (3.35.0). Supply `SignatureVerifyOptions.TrustStore` to evaluate the signer's certificate chain. |
| `ModifiedAfterSigning` | The signed revision is intact, but the file was changed after signing in a way other than adding later signatures or validation data, or the signature's byte range leaves part of the file unsigned (3.35.0). What a reader sees is not what was signed; see `SignatureVerificationResult.ModificationsAfterSigning`. |

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationStatus.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
