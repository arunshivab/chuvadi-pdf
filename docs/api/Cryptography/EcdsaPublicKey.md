# EcdsaPublicKey

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

An ECDSA public key — a point on a named curve.

```csharp
public sealed class EcdsaPublicKey : IPublicKey
```

## Constructors

### `EcdsaPublicKey(EcPoint publicPoint)`

Initialises a new EcdsaPublicKey from a curve and a public point.

## Properties

### `Algorithm`

```csharp
PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Ecdsa
```

<inheritdoc/>

### `PublicPoint`

```csharp
EcPoint PublicPoint
```

The public-key point on the curve.

### `Curve`

```csharp
EcCurve Curve => PublicPoint.Curve
```

The curve this key is defined over.

## Methods

### `FromUncompressedPoint`

__static__

```csharp
static EcdsaPublicKey FromUncompressedPoint(EcCurve curve, byte[] ecPoint)
```

Parses a SEC 1 §2.3.3 ECPoint octet string.

**Remarks:** Recognises the uncompressed form `04 || X || Y` where X and Y are each `FieldSizeBytes` big-endian unsigned integers. Compressed (`02`/`03`) and hybrid (`06`/`07`) forms are intentionally not supported — they require a square-root computation in the field that's rarely seen for ECDSA signatures in PDF context.

### `FromSubjectPublicKeyInfo`

__static__

```csharp
static EcdsaPublicKey FromSubjectPublicKeyInfo(SubjectPublicKeyInfo spki)
```

Parses an ECDSA public key from an X.509 SubjectPublicKeyInfo.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcdsaPublicKey.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcdsaPublicKey.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
