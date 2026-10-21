# CmsSignedDataBuilder

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

Builds a CMS SignedData (RFC 5652 §5) wrapped in a ContentInfo, ready for embedding in a PDF signature dictionary's `/Contents`.

```csharp
public static class CmsSignedDataBuilder
```

## Remarks

The output structure is: 
```
 ContentInfo { contentType: id-signedData (1.2.840.113549.1.7.2), content [0] EXPLICIT SignedData { version: 1, digestAlgorithms: SET OF { the signer's digestAlgorithm }, encapContentInfo { eContentType: id-data  (detached signature, no eContent), }, certificates [0] IMPLICIT SET OF Certificate, signerInfos SET OF { the one SignerInfo } } } 
```
  

 SignerInfo always includes the signed attributes `contentType` and `messageDigest` (mandatory per RFC 5652 §11) plus `signingTime` when supplied. The signature is computed over the DER encoding of the signed-attributes SET with the SET tag (0x31), not the [0] IMPLICIT tag that the wire encoding uses — this is the RFC 5652 §5.4 distinction Chuvadi's verifier already handles.

## Methods

### `BuildDetached`

__static__

```csharp
static byte[] BuildDetached(byte[] dataToSign, ISigner signer, DateTimeOffset? signingTime = null, IEnumerable<X509Certificate>? extraCertificates = null, IReadOnlyList<byte[]>? unsignedAttributes = null, IReadOnlyList<byte[]>? extraSignedAttributes = null)
```

Builds a detached CMS SignedData over `dataToSign`.

**Parameters**

- `dataToSign` — The bytes that the SignedData asserts a signature over.
- `signer` — The signer.
- `signingTime` — Optional signing time; included as a signed attribute when set.
- `extraCertificates` — Additional certs to embed in the SignedData (typically the signer's CA chain). The signer's own cert is always included.
- `unsignedAttributes` — Optional pre-encoded SEQUENCE-DER unsigned attributes (RFC 5652 §5.3); each entry is a complete `SEQUENCE { attrType OID, attrValues SET OF AttributeValue }`. Use `BuildSignatureTimeStampAttribute` for the RFC 3161 signature timestamp attribute.
- `extraSignedAttributes` — Optional pre-encoded signed attributes to add to the SignedAttributes set (in addition to the canonical content-type / message-digest / signing-time entries). Used to opt into CAdES profiles via `BuildSigningCertificateV2Attribute`.

**Returns:** DER bytes of the CMS ContentInfo wrapping the SignedData. These bytes are what goes into a PDF signature dictionary's `/Contents`.

### `BuildDetachedWithTimestamp`

__static__

```csharp
static byte[] BuildDetachedWithTimestamp(byte[] dataToSign, ISigner signer, Chuvadi.Cryptography.Timestamps.ITsaClient tsaClient, DateTimeOffset? signingTime = null, IEnumerable<X509Certificate>? extraCertificates = null, IReadOnlyList<byte[]>? additionalUnsignedAttributes = null)
```

Builds a detached CMS SignedData and embeds an RFC 3161 signature timestamp from `tsaClient` as an unsigned attribute.

**Remarks:** The workflow:  
 
- Build the SignerInfo's signature exactly as `BuildDetached` does. 
- Request a timestamp from `tsaClient` over the raw signature bytes (the value of `SignerInfo.signature`), per RFC 3161 §3.3.3.1. 
- Embed the resulting TimeStampToken as an `id-aa-signatureTimeStampToken` unsigned attribute on the SignerInfo.  

 The same hash algorithm as the signer's is used for the TSA's MessageImprint. Callers wanting fine-grained control over the TSA request can invoke `BuildDetached` directly, then build the attribute via `BuildSignatureTimeStampAttribute` and emit a new CMS.  <exception cref="Chuvadi.Cryptography.Timestamps.TsaException"> If the TSA refuses the request or returns a non-granted status. </exception>

### `BuildDetachedWithTimestampAsync`

__static__

```csharp
static async System.Threading.Tasks.Task<byte[]> BuildDetachedWithTimestampAsync(byte[] dataToSign, ISigner signer, Chuvadi.Cryptography.Timestamps.IAsyncTsaClient tsaClient, DateTimeOffset? signingTime = null, IEnumerable<X509Certificate>? extraCertificates = null, IReadOnlyList<byte[]>? additionalUnsignedAttributes = null, System.Threading.CancellationToken cancellationToken = default)
```

Async variant of `BuildDetachedWithTimestamp`: uses an `Chuvadi.Cryptography.Timestamps.IAsyncTsaClient` so the TSA fetch does not block.

### `BuildSignatureTimeStampAttribute`

__static__

```csharp
static byte[] BuildSignatureTimeStampAttribute(byte[] timeStampTokenDer)
```

Builds an `id-aa-signatureTimeStampToken` unsigned attribute (RFC 3161 §3.3.3.1, OID 1.2.840.113549.1.9.16.2.14) wrapping a pre-fetched RFC 3161 timestamp token. The result is suitable for passing in the `unsignedAttributes` parameter of `BuildDetached`.

**Parameters**

- `timeStampTokenDer` — The DER bytes of the TSA's TimeStampToken (a ContentInfo wrapping a SignedData).

### `BuildSigningCertificateV2Attribute`

__static__

```csharp
static byte[] BuildSigningCertificateV2Attribute(X509Certificate certificate, Chuvadi.Cryptography.Hashing.HashAlgorithmName hashAlgorithm)
```

Builds the `id-aa-signingCertificateV2` signed attribute (RFC 5035) for the supplied certificate, hashed with `hashAlgorithm`. Pass the result via `extraSignedAttributes` on `BuildDetached` to opt into the CAdES-BES profile.

**Parameters**

- `certificate` — The signer's certificate.
- `hashAlgorithm` — Hash algorithm used to compute the certificate digest. SHA-256 is the spec default; other values are emitted explicitly.

**Remarks:** The attribute binds the signer's certificate identity into the signed data so substitution attacks against the SignerInfo's IssuerAndSerialNumber become detectable.

---

_Source: [`src/Chuvadi.Cryptography/Cms/CmsSignedDataBuilder.cs`](../../../src/Chuvadi.Cryptography/Cms/CmsSignedDataBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
