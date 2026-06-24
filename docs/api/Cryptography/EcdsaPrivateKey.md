# EcdsaPrivateKey

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

An ECDSA private key — a scalar d in [1, n-1] on a fixed curve.

```csharp
public sealed class EcdsaPrivateKey
```

## Remarks

Curves supported: NIST P-256, P-384, P-521. Loaded from PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) via `FromPkcs8`, or from the bare RFC 5915 `ECPrivateKey` DER via `FromEcPrivateKey`.

## Constructors

### `EcdsaPrivateKey(EcCurve curve, BigInteger d)`

Initialises a new ECDSA private key.

**Parameters**

- `curve` — The curve the scalar lives on.
- `d` — The private scalar; must be in [1, n-1].

## Properties

### `Curve`

```csharp
EcCurve Curve
```

The elliptic curve.

### `D`

```csharp
BigInteger D
```

The private scalar d.

## Methods

### `FromPkcs8`

__static__

```csharp
static EcdsaPrivateKey FromPkcs8(byte[] pkcs8Der)
```

Parses a PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) carrying an ECPrivateKey payload. The curve is identified by the `privateKeyAlgorithm.parameters` field.

### `FromEcPrivateKey`

__static__

```csharp
static EcdsaPrivateKey FromEcPrivateKey(byte[] ecPrivateKeyDer, EcCurve curve)
```

Parses an RFC 5915 `ECPrivateKey` DER. The curve must be supplied because the structure's curve `parameters` field is optional.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcdsaPrivateKey.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcdsaPrivateKey.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
