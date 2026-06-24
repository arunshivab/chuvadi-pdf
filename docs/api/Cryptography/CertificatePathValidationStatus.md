# CertificatePathValidationStatus

**Enum** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

The outcome of validating a single certificate path.

```csharp
public enum CertificatePathValidationStatus
```

## Values

| Name | Description |
|---|---|
| `Valid` | The path validates: every link is sound and chains to a trust anchor. |
| `NoPathFound` | No path from the leaf to any trust anchor was found. |
| `SignatureInvalid` | Signature verification failed on at least one link in the chain. |
| `CertificateExpired` | A certificate in the path has expired at the validation time. |
| `CertificateNotYetValid` | A certificate in the path is not yet valid at the validation time. |
| `IntermediateNotACa` | An intermediate certificate's BasicConstraints does not assert cA=TRUE. |
| `LeafKeyUsageInvalid` | The leaf certificate is missing the digitalSignature key-usage bit. |
| `IntermediateKeyUsageInvalid` | An intermediate certificate is missing the keyCertSign key-usage bit. |
| `PathLengthExceeded` | A path-length constraint was exceeded. |
| `UnsupportedCriticalExtension` | A critical extension in some certificate is not recognised by Chuvadi. |
| `NameChainBroken` | Name chaining is broken: an issuer DN does not match the next subject DN. |
| `CertificateRevoked` | A certificate in the path is on a CRL. |

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePathValidationStatus.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePathValidationStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
