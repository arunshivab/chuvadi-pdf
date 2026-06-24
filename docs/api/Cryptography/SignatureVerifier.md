# SignatureVerifier

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Top-level signature-verification dispatcher.

```csharp
public static class SignatureVerifier
```

## Remarks

Given an algorithm identifier (typically from a CMS SignerInfo's signatureAlgorithm field), a public key, the message hash, and the signature bytes, dispatches to `RsaVerifier` or `EcdsaVerifier` with the correct hash algorithm and PSS parameters where applicable.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/SignatureVerifier.cs`](../../../src/Chuvadi.Cryptography/PublicKey/SignatureVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
