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

### `BuildSignatureTimeStampAttribute`

__static__

```csharp
static byte[] BuildSignatureTimeStampAttribute(byte[] timeStampTokenDer)
```

Builds an `id-aa-signatureTimeStampToken` unsigned attribute (RFC 3161 §3.3.3.1, OID 1.2.840.113549.1.9.16.2.14) wrapping a pre-fetched RFC 3161 timestamp token. The result is suitable for passing in the `unsignedAttributes` parameter of `BuildDetached`.

**Parameters**

- `timeStampTokenDer` — The DER bytes of the TSA's TimeStampToken (a ContentInfo wrapping a SignedData).

---

_Source: [`src/Chuvadi.Cryptography/Cms/CmsSignedDataBuilder.cs`](../../../src/Chuvadi.Cryptography/Cms/CmsSignedDataBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
