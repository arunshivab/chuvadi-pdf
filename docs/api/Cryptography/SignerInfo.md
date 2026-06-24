# SignerInfo

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

One signer's contribution to a SignedData structure.

```csharp
public sealed class SignerInfo
```

## Remarks

Structure: 
```
 SignerInfo ::= SEQUENCE { version            CMSVersion, sid                SignerIdentifier, digestAlgorithm    DigestAlgorithmIdentifier, signedAttrs    [0] IMPLICIT SignedAttributes OPTIONAL, signatureAlgorithm SignatureAlgorithmIdentifier, signature          SignatureValue, unsignedAttrs  [1] IMPLICIT UnsignedAttributes OPTIONAL } CMSVersion ::= INTEGER { v0(0), v1(1), v2(2), v3(3), v4(4), v5(5) } SignatureValue ::= OCTET STRING 
```
 Verification flow (which lands in a later commit): 
 
- Locate the signer's certificate by matching `SignerId` against SignedData.Certificates. 
- If `SignedAttributes` is present, compute the digest of the eContent (or detached byte range) under `DigestAlgorithm` and check it matches the messageDigest signed attribute. 
- Compute the digest of `CmsAttributeTable.DerEncodedForVerification` (or the eContent when no signed attrs) under `DigestAlgorithm`. 
- Verify `Signature` over that digest using `SignatureAlgorithm` and the signer's public key.

## Properties

### `Version`

```csharp
int Version
```

The CMS version: v1 (1) for IssuerAndSerial, v3 (3) for SKI.

### `SignerId`

```csharp
SignerIdentifier SignerId
```

Identifies which certificate produced this signature.

### `DigestAlgorithm`

```csharp
AlgorithmIdentifier DigestAlgorithm
```

The digest algorithm used over the signed content / attributes.

### `SignedAttributes`

```csharp
CmsAttributeTable? SignedAttributes
```

The signed attributes (signature actually covers their DER encoding).

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

The signature algorithm (combination of digest and key algorithm).

### `Signature`

```csharp
byte[] Signature
```

The raw signature bytes.

### `UnsignedAttributes`

```csharp
CmsAttributeTable? UnsignedAttributes
```

The unsigned attributes (often carries the RFC 3161 timestamp token).

### `HasSignedAttributes`

```csharp
bool HasSignedAttributes => SignedAttributes is not null
```

True when this SignerInfo uses signed attributes (the standard CMS profile).

## Methods

### `FindSignerCertificate`

```csharp
X509Certificate? FindSignerCertificate(System.Collections.Generic.IEnumerable<X509Certificate> candidates)
```

Locates the signer's certificate by matching `SignerId` against the given collection. Returns null when no match exists.

### `Read`

__static__

```csharp
static SignerInfo Read(Asn1Reader reader)
```

Reads a SignerInfo from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/SignerInfo.cs`](../../../src/Chuvadi.Cryptography/Cms/SignerInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
