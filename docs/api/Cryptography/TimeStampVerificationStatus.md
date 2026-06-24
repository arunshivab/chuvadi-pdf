# TimeStampVerificationStatus

**Enum** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

Outcome of `TimeStampTokenVerifier`.

```csharp
public enum TimeStampVerificationStatus
```

## Values

| Name | Description |
|---|---|
| `Valid` | The TST is cryptographically valid: TSTInfo's signed bytes match the signer's signature. |
| `SignerCertificateNotFound` | The TST's signer certificate is not embedded in the token. |
| `SignerNotAuthorisedForTimestamping` | The signer's certificate is missing the id-kp-timeStamping extended key usage. |
| `DigestMismatch` | The signature digest does not match. |
| `SignatureInvalid` | The signature does not verify against the signer's public key. |
| `MessageImprintMismatch` | The TST's messageImprint does not match the bytes the caller said it should cover. |
| `UnsupportedAlgorithm` | The TST uses algorithms Chuvadi does not implement. |
| `MalformedToken` | The TST envelope could not be parsed. |

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
