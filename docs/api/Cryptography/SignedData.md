# SignedData

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

A decoded CMS SignedData structure.

```csharp
public sealed class SignedData
```

## Remarks

Structure: 
```
 SignedData ::= SEQUENCE { version           CMSVersion, digestAlgorithms  DigestAlgorithmIdentifiers, encapContentInfo  EncapsulatedContentInfo, certificates  [0] IMPLICIT CertificateSet OPTIONAL, crls          [1] IMPLICIT RevocationInfoChoices OPTIONAL, signerInfos       SignerInfos } DigestAlgorithmIdentifiers ::= SET OF DigestAlgorithmIdentifier SignerInfos ::= SET OF SignerInfo CertificateSet ::= SET OF CertificateChoices 
```
 For PDF signatures the typical shape is: 
 
- One digestAlgorithm (SHA-256 or SHA-384 in modern signatures, SHA-1 in legacy). 
- One EncapsulatedContentInfo with eContentType = id-data and absent eContent (detached). 
- Certificates set containing the signer's cert and (typically) the issuing CA chain. 
- One SignerInfo with signedAttrs containing contentType, messageDigest, signingTime, and SigningCertificateV2 (CAdES baseline).  CRLs in the SignedData are rare; revocation information for CAdES typically arrives via the revocationValues unsigned attribute.

## Properties

### `Version`

```csharp
int Version
```

The CMS version (1 for typical PDF signatures, 3 for SKI signers).

### `EncapContentInfo`

```csharp
EncapsulatedContentInfo EncapContentInfo
```

The encapsulated content (or its absence, for detached signatures).

## Methods

### `new`

```csharp
ReadOnlyCollection<AlgorithmIdentifier> DigestAlgorithms => new(_digestAlgorithms)
```

The set of digest algorithms used by any SignerInfo.

### `new`

```csharp
ReadOnlyCollection<X509Certificate> Certificates => new(_certificates)
```

The certificates embedded in the SignedData.

### `new`

```csharp
ReadOnlyCollection<byte[]> Crls => new(_crls)
```

The CRLs embedded in the SignedData (raw bytes — CRL decoder lands later).

### `new`

```csharp
ReadOnlyCollection<SignerInfo> SignerInfos => new(_signerInfos)
```

The SignerInfos.

### `Read`

__static__

```csharp
static SignedData Read(Asn1Reader reader)
```

Reads a SignedData from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/SignedData.cs`](../../../src/Chuvadi.Cryptography/Cms/SignedData.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
