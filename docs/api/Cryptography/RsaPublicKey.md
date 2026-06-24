# RsaPublicKey

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

An RSA public key — modulus n and public exponent e.

```csharp
public sealed class RsaPublicKey : IPublicKey
```

## Remarks

ASN.1 (RFC 8017 §3.1): 
```
 RSAPublicKey ::= SEQUENCE { modulus         INTEGER,  -- n publicExponent  INTEGER   -- e } 
```
 Inside an X.509 SubjectPublicKeyInfo the algorithm OID is `Oids.KnownOids.RsaEncryption` (1.2.840.113549.1.1.1) and the BIT STRING contents are exactly the DER encoding above.

## Constructors

### `RsaPublicKey(BigInteger modulus, BigInteger publicExponent)`

Initialises a new RsaPublicKey.

## Properties

### `Algorithm`

```csharp
PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Rsa
```

<inheritdoc/>

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

## Methods

### `FromSubjectPublicKey`

__static__

```csharp
static RsaPublicKey FromSubjectPublicKey(byte[] subjectPublicKey)
```

Parses an RSA public key from the BIT STRING contents of a SubjectPublicKeyInfo.

**Remarks:** The input is the raw bytes inside the BIT STRING (i.e. the DER encoding of the RSAPublicKey SEQUENCE), not the BIT STRING itself.

### `FromSubjectPublicKeyInfo`

__static__

```csharp
static RsaPublicKey FromSubjectPublicKeyInfo(SubjectPublicKeyInfo spki)
```

Parses an RSA public key from a SubjectPublicKeyInfo container. <exception cref="ArgumentException"> Thrown when the SubjectPublicKeyInfo algorithm is not RSA, or the BIT STRING has padding bits. </exception>

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/RsaPublicKey.cs`](../../../src/Chuvadi.Cryptography/PublicKey/RsaPublicKey.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
