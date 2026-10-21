# RsaSigner

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Hand-rolled RSASSA-PKCS1-v1_5 signing per RFC 8017 §8.2.

```csharp
public static class RsaSigner
```

## Remarks

Implementation is the textbook RSASP1 primitive (modular exponentiation with the private exponent) wrapped in EMSA-PKCS1-v1_5 encoding. CRT is not yet applied; signing operates on the full (n, d) pair. CRT will be added in a future session for performance, not correctness.

## Methods

### `SignPkcs1v15`

__static__

```csharp
static byte[] SignPkcs1v15(RsaPrivateKey privateKey, HashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> messageHash)
```

Signs a pre-computed message digest using RSASSA-PKCS1-v1_5.

**Parameters**

- `privateKey` — The RSA private key.
- `hashAlgorithm` — The hash algorithm that produced the digest.
- `messageHash` — The message digest to sign.

**Returns:** The signature bytes, length = modulus size.

### `SignPkcs1v15`

__static__

```csharp
static byte[] SignPkcs1v15(RsaPrivateKey privateKey, HashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> message, bool hashIsAlreadyDigest)
```

Hashes `message` with `hashAlgorithm` then signs the digest via `SignPkcs1v15(RsaPrivateKey, HashAlgorithmName, ReadOnlySpan{byte})`.

### `SignPss`

__static__

```csharp
static byte[] SignPss(RsaPrivateKey privateKey, HashAlgorithmName hashAlgorithm, HashAlgorithmName mgfHashAlgorithm, int saltLength, ReadOnlySpan<byte> messageHash)
```

Signs `messageHash` with RSASSA-PSS per RFC 8017 §8.1.

**Parameters**

- `privateKey` — The signing key.
- `hashAlgorithm` — Hash used to derive `messageHash`.
- `mgfHashAlgorithm` — Hash used for MGF1 (commonly equal to `hashAlgorithm`).
- `saltLength` — Salt length in bytes (commonly the hash output size).
- `messageHash` — The message digest.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/RsaSigner.cs`](../../../src/Chuvadi.Cryptography/PublicKey/RsaSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
