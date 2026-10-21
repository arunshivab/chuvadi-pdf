# RsaPkcs1V15Signer

**Class** in `Chuvadi.Cryptography.Signing` (Cryptography)

An `ISigner` implementation backed by Chuvadi's hand-rolled RSASSA-PKCS1-v1_5 signing primitive (`RsaSigner`).

```csharp
public sealed class RsaPkcs1V15Signer : ISigner
```

## Remarks

Loaded from a PKCS#8 unencrypted private key plus the matching X.509 certificate. The signature algorithm OID is chosen from the hash: 
 
- SHA-256 → sha256WithRSAEncryption (1.2.840.113549.1.1.11) 
- SHA-384 → sha384WithRSAEncryption (1.2.840.113549.1.1.12) 
- SHA-512 → sha512WithRSAEncryption (1.2.840.113549.1.1.13)

## Constructors

### `RsaPkcs1V15Signer(RsaPrivateKey privateKey, X509Certificate certificate, HashAlgorithmName hashAlgorithm)`

Initialises a new signer.

**Parameters**

- `privateKey` — The RSA private key.
- `certificate` — The X.509 certificate whose public key matches `privateKey`. Chuvadi does not verify that the cert and key match — it is the caller's responsibility to ensure consistency.
- `hashAlgorithm` — SHA-256, SHA-384, or SHA-512.

## Properties

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

_Source: [`src/Chuvadi.Cryptography/Signing/RsaPkcs1V15Signer.cs`](../../../src/Chuvadi.Cryptography/Signing/RsaPkcs1V15Signer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
