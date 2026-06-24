# CertificateListSignatureVerifier

**Class** in `Chuvadi.Cryptography.Revocation` (Cryptography)

Verifies the signature on a `CertificateList` against the issuing CA's public key.

```csharp
public static class CertificateListSignatureVerifier
```

## Methods

### `Verify`

__static__

```csharp
static bool Verify(CertificateList crl, SubjectPublicKeyInfo issuerPublicKeyInfo)
```

Verifies `crl`'s signature using `issuerPublicKeyInfo`.

**Returns:** True iff the signature is cryptographically valid.

---

_Source: [`src/Chuvadi.Cryptography/Revocation/CertificateListSignatureVerifier.cs`](../../../src/Chuvadi.Cryptography/Revocation/CertificateListSignatureVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
