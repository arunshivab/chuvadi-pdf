# TimeStampTokenVerifier

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

Verifies an RFC 3161 TimeStampToken cryptographically and against a known message-imprint payload.

```csharp
public static class TimeStampTokenVerifier
```

## Methods

### `Verify`

__static__

```csharp
static TimeStampVerificationResult Verify(TimeStampToken token, byte[] expectedSignedBytes)
```

Verifies the token's CMS signature and confirms its messageImprint hashes `expectedSignedBytes`.

**Parameters**

- `token` — The token to verify.
- `expectedSignedBytes` — The bytes the caller expected the TSA to have hashed. For an Adobe-style signatureTimeStampToken on a PDF, this is the SignerInfo.signature bytes of the outer CMS (i.e. the PDF signer's signature value).

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
