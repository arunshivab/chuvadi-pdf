# EcdsaCmsSigner

**Class** in `Chuvadi.Cryptography.Signing` (Cryptography)

An `ISigner` backed by Chuvadi's hand-rolled ECDSA primitive (`Chuvadi.Cryptography.PublicKey.EcdsaSigner`).

```csharp
public sealed class EcdsaCmsSigner : ISigner
```

## Remarks

The signature algorithm OID is chosen from the hash: 
 
- SHA-256 → ecdsa-with-SHA256 (1.2.840.10045.4.3.2) 
- SHA-384 → ecdsa-with-SHA384 (1.2.840.10045.4.3.3) 
- SHA-512 → ecdsa-with-SHA512 (1.2.840.10045.4.3.4)  Any of these can be paired with any supported curve (P-256, P-384, P-521).

## Constructors

### `EcdsaCmsSigner(EcdsaPrivateKey privateKey, X509Certificate certificate, HashAlgorithmName hashAlgorithm)`

Initialises a new signer.

**Parameters**

- `privateKey` — The ECDSA private key.
- `certificate` — The X.509 certificate whose public key matches `privateKey`. Chuvadi does not verify that the cert and key match — it is the caller's responsibility to ensure consistency.
- `hashAlgorithm` — SHA-256, SHA-384, or SHA-512.

### `EcdsaCmsSigner(EcdsaPrivateKey privateKey, X509Certificate certificate, HashAlgorithmName hashAlgorithm, bool deterministic)`

Same as the three-argument constructor but lets the caller opt into RFC 6979 deterministic nonces. When `deterministic` is true, signatures are bit-for-bit reproducible from the same inputs and immune to RNG failures.

## Properties

### `Deterministic`

```csharp
bool Deterministic
```

True when nonces are derived per RFC 6979.

### `Certificate`

```csharp
X509Certificate Certificate
```

<inheritdoc />

### `HashAlgorithm`

```csharp
HashAlgorithmName HashAlgorithm
```

<inheritdoc />

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

<inheritdoc />

## Methods

### `Sign`

```csharp
byte[] Sign(byte[] dataToSign)
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Cryptography/Signing/EcdsaSigner.cs`](../../../src/Chuvadi.Cryptography/Signing/EcdsaSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
