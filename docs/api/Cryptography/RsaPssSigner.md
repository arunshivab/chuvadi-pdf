# RsaPssSigner

**Class** in `Chuvadi.Cryptography.Signing` (Cryptography)

An `ISigner` backed by Chuvadi's RSASSA-PSS primitive.

```csharp
public sealed class RsaPssSigner : ISigner
```

## Remarks

RSASSA-PSS is the modern RSA signature scheme; unlike PKCS#1 v1.5 it uses a probabilistic encoding (EMSA-PSS), which provides a tight security reduction to the RSA problem. The PSS parameters (hash algorithm, MGF1 hash algorithm, salt length) are encoded into the X.509 `AlgorithmIdentifier` so verifiers know exactly which parameter set to use.  

 Conventional defaults: MGF1 hash = signing hash; salt length = digest size. The constructor takes these as parameters in case the caller needs a non-default profile.

## Properties

### `Certificate`

```csharp
X509Certificate Certificate
```

<inheritdoc/>

### `HashAlgorithm`

```csharp
HashAlgorithmName HashAlgorithm
```

<inheritdoc/>

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

<inheritdoc/>

## Methods

### `Sign`

```csharp
byte[] Sign(byte[] dataToSign)
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Cryptography/Signing/RsaPssSigner.cs`](../../../src/Chuvadi.Cryptography/Signing/RsaPssSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
