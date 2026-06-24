# Hmac

**Class** in `Chuvadi.Cryptography.Hashing` (Cryptography)

HMAC keyed-hash message authentication code per RFC 2104.

```csharp
public static class Hmac
```

## Remarks

Implementation directly follows RFC 2104 §2. The key is reduced to blocksize bytes (hashed if longer, zero-padded if shorter); inner and outer pads are XORed with 0x36 and 0x5C respectively; the MAC is `H(K xor opad || H(K xor ipad || message))`.

---

_Source: [`src/Chuvadi.Cryptography/Hashing/Hmac.cs`](../../../src/Chuvadi.Cryptography/Hashing/Hmac.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
