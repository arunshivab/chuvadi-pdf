# CertificateList

**Class** in `Chuvadi.Cryptography.Revocation` (Cryptography)

A parsed X.509 Certificate Revocation List (CRL).

```csharp
public sealed class CertificateList
```

## Remarks

RFC 5280 §5.1 defines a CRL as: 
```
 CertificateList ::= SEQUENCE { tbsCertList         TBSCertList, signatureAlgorithm  AlgorithmIdentifier, signatureValue      BIT STRING } 
```
 

 This decoder accepts v1 and v2 CRLs but rejects delta CRLs (indicated by the `deltaCRLIndicator` extension being present). Indirect CRLs (with `issuingDistributionPoint`'s `indirectCRL` flag set) are also rejected — Chuvadi assumes the CRL is issued by the certificate's own issuer. A future session will lift these restrictions.

## Properties

### `Version`

```csharp
int Version
```

CRL version: 1 (encoded as 0) or 2 (encoded as 1).

### `TbsSignatureAlgorithm`

```csharp
AlgorithmIdentifier TbsSignatureAlgorithm
```

The signature algorithm declared inside TBSCertList.

### `Issuer`

```csharp
X509Name Issuer
```

The issuing CA's distinguished name.

### `ThisUpdate`

```csharp
DateTimeOffset ThisUpdate
```

The time this CRL was issued.

### `NextUpdate`

```csharp
DateTimeOffset? NextUpdate
```

The time by which the next CRL will be issued. May be absent.

### `CrlNumber`

```csharp
BigInteger? CrlNumber
```

The CRL's `crlNumber` extension value, when present. Useful for ordering: a CRL with a higher number supersedes one with a lower number from the same issuer.

### `TbsRawEncoding`

```csharp
byte[] TbsRawEncoding
```

The raw DER bytes of TBSCertList — hashed for signature verification.

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

The outer signatureAlgorithm. Must equal `TbsSignatureAlgorithm`.

### `SignatureValue`

```csharp
BitStringValue SignatureValue
```

The signature over TBSCertList.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The DER encoding of the whole CertificateList.

## Methods

### `IsRevoked`

```csharp
bool IsRevoked(BigInteger serial) => _bySerial.ContainsKey(serial)
```

True iff `serial` appears in this CRL's revoked list.

### `FindRevocation`

```csharp
RevokedCertificate? FindRevocation(BigInteger serial)
```

Returns the revocation entry for `serial`, or null if not revoked.

### `Decode`

__static__

```csharp
static CertificateList Decode(byte[] der)
```

Parses a CertificateList from its DER encoding. <exception cref="Asn1Exception">If the bytes are not a well-formed CRL.</exception> <exception cref="NotSupportedException">If the CRL is a delta CRL or indirect CRL.</exception>

---

_Source: [`src/Chuvadi.Cryptography/Revocation/CertificateList.cs`](../../../src/Chuvadi.Cryptography/Revocation/CertificateList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
