# EcdsaSigner

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Hand-rolled ECDSA signing per FIPS 186-4 §6.4.

```csharp
public static class EcdsaSigner
```

## Remarks

Implementation is the textbook ECDSA primitive: hash the message, truncate to `n`'s bit length, generate a random nonce k via `RandomNumberGenerator`, compute (r, s), and encode as a DER SEQUENCE of two INTEGERs per RFC 3279 §2.2.3.  

 Nonces are sampled from `RandomNumberGenerator` with rejection sampling; RFC 6979 deterministic nonces are not yet implemented (a future-session improvement, perf-neutral but more reproducible).

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcdsaSigner.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcdsaSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
