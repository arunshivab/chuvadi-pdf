# IPublicKey

**Interface** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Marker interface implemented by all Chuvadi public-key types.

```csharp
public interface IPublicKey
```

## Remarks

Concrete implementations carry the algorithm-specific key material (RSA modulus + exponent, ECDSA point + curve, etc.). A SignatureVerifier dispatches to the right verification routine based on the runtime type.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/IPublicKey.cs`](../../../src/Chuvadi.Cryptography/PublicKey/IPublicKey.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
