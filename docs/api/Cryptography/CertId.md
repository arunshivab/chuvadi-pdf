# CertId

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

Identifies a certificate inside an OCSP response.

```csharp
public sealed class CertId
```

## Remarks

RFC 6960 §4.1.1: 
```
 CertID ::= SEQUENCE { hashAlgorithm   AlgorithmIdentifier, issuerNameHash  OCTET STRING,  -- Hash of issuer's DN issuerKeyHash   OCTET STRING,  -- Hash of issuer's public key serialNumber    CertificateSerialNumber } 
```
 The two hashes are computed over the issuer cert's `tbsCertificate.subject` (DER bytes) and the `BIT STRING` content of its `subjectPublicKey` respectively, using `hashAlgorithm`.

## Properties

### `HashAlgorithm`

```csharp
AlgorithmIdentifier HashAlgorithm
```

Hash algorithm used to compute the two issuer-derived hashes.

### `IssuerNameHash`

```csharp
byte[] IssuerNameHash
```

Hash of the issuer's distinguished name.

### `IssuerKeyHash`

```csharp
byte[] IssuerKeyHash
```

Hash of the issuer's public key.

### `SerialNumber`

```csharp
BigInteger SerialNumber
```

The subject certificate's serial number.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/CertId.cs`](../../../src/Chuvadi.Cryptography/Ocsp/CertId.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
