# EcdsaSigner

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Hand-rolled ECDSA signing per FIPS 186-4 §6.4.

```csharp
public static class EcdsaSigner
```

## Remarks

Implementation is the textbook ECDSA primitive: hash the message, truncate to `n`'s bit length, generate a random nonce k via `RandomNumberGenerator`, compute (r, s), and encode as a DER SEQUENCE of two INTEGERs per RFC 3279 §2.2.3.  

 Nonces are sampled from `RandomNumberGenerator` with rejection sampling; RFC 6979 deterministic nonces are not yet implemented (a future-session improvement, perf-neutral but more reproducible).

## Methods

### `Sign`

__static__

```csharp
static byte[] Sign(EcdsaPrivateKey privateKey, ReadOnlySpan<byte> messageHash)
```

Signs a pre-computed message digest using ECDSA.

**Parameters**

- `privateKey` — The ECDSA private key.
- `messageHash` — The message digest.

**Returns:** The DER-encoded `(r, s)` signature.

### `Sign`

__static__

```csharp
static byte[] Sign(EcdsaPrivateKey privateKey, ChuvadiHashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> message, bool hashIsAlreadyDigest)
```

Hashes `message` with `hashAlgorithm` then signs the digest.

### `SignDeterministic`

__static__

```csharp
static byte[] SignDeterministic(EcdsaPrivateKey privateKey, ReadOnlySpan<byte> messageHash, ChuvadiHashAlgorithmName hashAlgorithm)
```

Converts a message hash into the integer `e` used by ECDSA. Per FIPS 186-4 §6.4: if the hash is longer than the bit-length of `n`, take the leftmost bit-length(n) bits. Signs a hash deterministically per RFC 6979 instead of using random nonces. Bit-for-bit reproducible for the same key + message + hash algorithm; immune to RNG failures.

**Parameters**

- `privateKey` — The signing key.
- `messageHash` — The hash of the message being signed.
- `hashAlgorithm` — The hash used for HMAC-DRBG inside RFC 6979.

**Returns:** The DER-encoded `(r, s)` signature.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcdsaSigner.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcdsaSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
