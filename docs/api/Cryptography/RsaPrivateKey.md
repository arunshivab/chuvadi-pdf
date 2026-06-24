# RsaPrivateKey

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

An RSA private key — modulus n, public exponent e, and private exponent d.

```csharp
public sealed class RsaPrivateKey
```

## Remarks

This class carries the minimum data needed for signing (n, e, d). Production CRT parameters (p, q, dP, dQ, qInv) per RFC 8017 §3.2 are not currently stored — signing operates via plain modular exponentiation. CRT will be added in a future session for performance.  

 Loaded from PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) via `FromPkcs8`, or from the bare RSAPrivateKey DER via `FromRsaPrivateKey`.

## Constructors

### `RsaPrivateKey(BigInteger modulus, BigInteger publicExponent, BigInteger privateExponent)`

Initialises a new RSA private key.

**Parameters**

- `modulus` — The RSA modulus n; must be positive.
- `publicExponent` — The RSA public exponent e; must be positive.
- `privateExponent` — The RSA private exponent d; must be positive.

## Properties

### `Modulus`

```csharp
BigInteger Modulus
```

The RSA modulus (n).

### `PublicExponent`

```csharp
BigInteger PublicExponent
```

The RSA public exponent (e), typically 65537.

### `PrivateExponent`

```csharp
BigInteger PrivateExponent
```

The RSA private exponent (d).

## Methods

### `=>`

```csharp
int ModulusSizeBytes => (int)((Modulus.GetBitLength() + 7) / 8)
```

The size of the modulus in bytes (k = ⌈log256 n⌉).

### `new`

```csharp
RsaPublicKey PublicKey => new(Modulus, PublicExponent)
```

The corresponding RSA public key.

### `FromPkcs8`

__static__

```csharp
static RsaPrivateKey FromPkcs8(byte[] pkcs8Der)
```

Parses a PKCS#8 unencrypted PrivateKeyInfo (RFC 5208) carrying an RSAPrivateKey payload. <exception cref="Asn1Exception">If the bytes are not a valid PKCS#8 RSA key.</exception>

### `FromRsaPrivateKey`

__static__

```csharp
static RsaPrivateKey FromRsaPrivateKey(byte[] rsaPrivateKeyDer)
```

Parses an RSAPrivateKey DER (RFC 8017 §3.2). Used internally by `FromPkcs8`; exposed for callers that have the raw key bytes without the PKCS#8 wrapper.

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/RsaPrivateKey.cs`](../../../src/Chuvadi.Cryptography/PublicKey/RsaPrivateKey.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
