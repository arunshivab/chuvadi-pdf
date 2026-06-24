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
