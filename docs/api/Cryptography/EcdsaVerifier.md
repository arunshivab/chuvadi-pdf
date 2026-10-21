# EcdsaVerifier

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Verifies ECDSA signatures per FIPS 186-4 §6.4.

```csharp
public static class EcdsaVerifier
```

## Methods

### `Verify`

__static__

```csharp
static bool Verify(EcdsaPublicKey publicKey, ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
```

Verifies an ECDSA signature.

**Parameters**

- `publicKey` — Signer's ECDSA public key.
- `messageHash` — The message digest (any hash; e is truncated as required).
- `signature` — DER-encoded SEQUENCE { INTEGER r, INTEGER s }.

**Returns:** True iff the signature is valid.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcdsaVerifier.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcdsaVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
