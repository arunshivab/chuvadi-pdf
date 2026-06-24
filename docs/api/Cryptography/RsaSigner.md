# RsaSigner

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Hand-rolled RSASSA-PKCS1-v1_5 signing per RFC 8017 §8.2.

```csharp
public static class RsaSigner
```

## Remarks

Implementation is the textbook RSASP1 primitive (modular exponentiation with the private exponent) wrapped in EMSA-PKCS1-v1_5 encoding. CRT is not yet applied; signing operates on the full (n, d) pair. CRT will be added in a future session for performance, not correctness.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/RsaSigner.cs`](../../../src/Chuvadi.Cryptography/PublicKey/RsaSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
