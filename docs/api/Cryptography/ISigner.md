# ISigner

**Interface** in `Chuvadi.Cryptography.Signing` (Cryptography)

A pluggable signing primitive used by `CmsSignedDataBuilder` to produce a CMS SignerInfo signature. Implementations may be backed by software keys, an HSM, a smartcard, or a remote signing service.

```csharp
public interface ISigner
```

## Remarks

Implementations are stateless from CMS's perspective: each call hashes a message and signs it. The signer carries the certificate it signs with so the CMS builder can identify the signer and decide which SubjectKeyIdentifier or IssuerAndSerialNumber to put in the SignerInfo.

---

_Source: [`src/Chuvadi.Cryptography/Signing/ISigner.cs`](../../../src/Chuvadi.Cryptography/Signing/ISigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
