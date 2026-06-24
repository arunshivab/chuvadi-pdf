# X509Certificate

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

A fully-decoded X.509 certificate.

```csharp
public sealed class X509Certificate
```

## Remarks

Structure: 
```
 Certificate ::= SEQUENCE { tbsCertificate     TBSCertificate, signatureAlgorithm AlgorithmIdentifier, signatureValue     BIT STRING } 
```
 Signature verification (which lands in a later commit) consists of: 
 
- Hashing `TbsCertificate.RawEncoding` with the algorithm identified by `SignatureAlgorithm`. 
- Verifying that hash against `SignatureValue` using the issuer's public key. 
- Confirming the algorithm declared in TBS (TbsCertificate.Signature) matches `SignatureAlgorithm` — RFC 5280 §4.1.1.2.

## Properties

### `Tbs`

```csharp
TbsCertificate Tbs
```

The TBS body — the bytes the signature actually covers.

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

The signature algorithm declared on the outer Certificate.

### `SignatureValue`

```csharp
BitStringValue SignatureValue
```

The signature value as a BIT STRING.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The complete DER encoding of the Certificate.

### `Subject`

```csharp
X509Name Subject => Tbs.Subject
```

Convenience accessor for the subject DN.

### `Issuer`

```csharp
X509Name Issuer => Tbs.Issuer
```

Convenience accessor for the issuer DN.

### `Validity`

```csharp
Validity Validity => Tbs.Validity
```

Convenience accessor for the validity period.

## Methods

### `Tbs.Signature.Equals`

```csharp
bool TbsAndOuterAlgorithmsMatch => Tbs.Signature.Equals(SignatureAlgorithm)
```

True when the algorithm in the TBS body matches the outer signatureAlgorithm. RFC 5280 §4.1.1.2: these MUST be equal.

### `Decode`

__static__

```csharp
static X509Certificate Decode(byte[] der)
```

Decodes an X509 certificate from its DER-encoded bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/X509Certificate.cs`](../../../src/Chuvadi.Cryptography/X509/X509Certificate.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
