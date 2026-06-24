# SubjectPublicKeyInfo

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The public key carried by an X.509 certificate, together with the algorithm identifier needed to interpret its bytes.

```csharp
public sealed class SubjectPublicKeyInfo
```

## Remarks

Structure: 
```
 SubjectPublicKeyInfo ::= SEQUENCE { algorithm         AlgorithmIdentifier, subjectPublicKey  BIT STRING } 
```
 The BIT STRING contents are algorithm-specific: 
 
- RSA: DER-encoded RSAPublicKey (modulus, exponent) — RFC 3279 §2.3.1. 
- ECDSA: an uncompressed (0x04 ‖ X ‖ Y) or compressed EC point — RFC 5480 §2.2. 
- Ed25519/Ed448: the raw 32 or 57 byte public key — RFC 8410 §4.  Chuvadi keeps the BIT STRING content unparsed here; specialised decoders will lift it into algorithm-specific public key types as those land.

## Constructors

### `SubjectPublicKeyInfo(AlgorithmIdentifier algorithm, BitStringValue subjectPublicKey, byte[] rawEncoding)`

Initialises a new SubjectPublicKeyInfo.

## Properties

### `Algorithm`

```csharp
AlgorithmIdentifier Algorithm
```

The algorithm identifier of the public key.

### `SubjectPublicKey`

```csharp
BitStringValue SubjectPublicKey
```

The public-key bytes, encoded as an algorithm-specific BIT STRING.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The complete DER encoding of the SubjectPublicKeyInfo (preserved for hashing).

## Methods

### `Read`

__static__

```csharp
static SubjectPublicKeyInfo Read(Asn1Reader reader)
```

Reads a SubjectPublicKeyInfo from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/SubjectPublicKeyInfo.cs`](../../../src/Chuvadi.Cryptography/X509/SubjectPublicKeyInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
