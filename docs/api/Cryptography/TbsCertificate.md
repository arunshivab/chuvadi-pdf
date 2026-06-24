# TbsCertificate

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The "to-be-signed" body of an X.509 certificate.

```csharp
public sealed class TbsCertificate
```

## Remarks

Structure: 
```
 TBSCertificate ::= SEQUENCE { version          [0] EXPLICIT Version DEFAULT v1, serialNumber         CertificateSerialNumber, signature            AlgorithmIdentifier, issuer               Name, validity             Validity, subject              Name, subjectPublicKeyInfo SubjectPublicKeyInfo, issuerUniqueID   [1] IMPLICIT UniqueIdentifier OPTIONAL, subjectUniqueID  [2] IMPLICIT UniqueIdentifier OPTIONAL, extensions       [3] EXPLICIT Extensions OPTIONAL } Version ::= INTEGER { v1(0), v2(1), v3(2) } 
```
 The RawEncoding property holds the exact bytes of this TBSCertificate SEQUENCE — these are the bytes whose hash the signatureValue covers. Preserving them losslessly is what makes signature verification possible.

## Properties

### `Version`

```csharp
int Version
```

The certificate version: 0=v1, 1=v2, 2=v3.

### `SerialNumber`

```csharp
BigInteger SerialNumber
```

The certificate serial number (unique within an issuer; can be any non-negative BigInteger).

### `Signature`

```csharp
AlgorithmIdentifier Signature
```

The signature algorithm declared inside the TBS body (must match the outer Certificate's algorithm).

### `Issuer`

```csharp
X509Name Issuer
```

The issuer distinguished name.

### `Validity`

```csharp
Validity Validity
```

The validity period.

### `Subject`

```csharp
X509Name Subject
```

The subject distinguished name.

### `SubjectPublicKeyInfo`

```csharp
SubjectPublicKeyInfo SubjectPublicKeyInfo
```

The subject's public key.

### `IssuerUniqueId`

```csharp
BitStringValue? IssuerUniqueId
```

Optional v2/v3 issuerUniqueID.

### `SubjectUniqueId`

```csharp
BitStringValue? SubjectUniqueId
```

Optional v2/v3 subjectUniqueID.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The complete TBSCertificate encoding — the bytes whose hash is signed by the outer Certificate.signatureValue.

## Methods

### `new`

```csharp
ReadOnlyCollection<X509Extension> Extensions => new(_extensions)
```

The v3 extensions.

### `FindExtension`

```csharp
X509Extension? FindExtension(ObjectIdentifier oid)
```

Finds an extension by OID, or returns null when absent.

### `Read`

__static__

```csharp
static TbsCertificate Read(Asn1Reader reader)
```

Reads a TBSCertificate from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/TbsCertificate.cs`](../../../src/Chuvadi.Cryptography/X509/TbsCertificate.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
