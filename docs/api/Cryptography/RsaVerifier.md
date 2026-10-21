# RsaVerifier

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

Verifies RSA signatures in PKCS#1 v1.5 (RSASSA-PKCS1-v1_5) and PSS (RSASSA-PSS) formats per RFC 8017.

```csharp
public static class RsaVerifier
```

## Methods

### `VerifyPkcs1v15`

__static__

```csharp
static bool VerifyPkcs1v15(RsaPublicKey publicKey, HashAlgorithmName hashAlgorithm, ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
```

Verifies a PKCS#1 v1.5 signature.

**Parameters**

- `publicKey` — The signer's RSA public key.
- `hashAlgorithm` — The hash algorithm used to digest the message.
- `messageHash` — The message digest (output of `hashAlgorithm`).
- `signature` — The signature bytes (length must equal modulus size).

**Returns:** True when the signature is valid; false otherwise.

### `VerifyPss`

__static__

```csharp
static bool VerifyPss(RsaPublicKey publicKey, HashAlgorithmName hashAlgorithm, HashAlgorithmName mgfHashAlgorithm, int saltLength, ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
```

Verifies an RSASSA-PSS signature.

**Parameters**

- `publicKey` — The signer's RSA public key.
- `hashAlgorithm` — The hash algorithm used to digest the message.
- `mgfHashAlgorithm` — The hash algorithm used inside MGF1 (typically same as `hashAlgorithm`).
- `saltLength` — The PSS salt length in bytes.
- `messageHash` — The message digest.
- `signature` — The signature bytes.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/RsaVerifier.cs`](../../../src/Chuvadi.Cryptography/PublicKey/RsaVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
