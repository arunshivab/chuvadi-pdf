# EncapsulatedContentInfo

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

The content being signed (attached) or referenced (detached) by a SignedData.

```csharp
public sealed class EncapsulatedContentInfo
```

## Remarks

Structure: 
```
 EncapsulatedContentInfo ::= SEQUENCE { eContentType    OBJECT IDENTIFIER, eContent    [0] EXPLICIT OCTET STRING OPTIONAL } 
```
 For PDF signatures the typical pattern is: 
 
- `adbe.pkcs7.detached` — eContentType = id-data (1.2.840.113549.1.7.1) and eContent is absent. The signed bytes are the PDF byte range, supplied out-of-band. 
- `ETSI.RFC3161` — eContentType = id-ct-TSTInfo (1.2.840.113549.1.9.16.1.4) and eContent contains the encoded TSTInfo.

## Constructors

### `EncapsulatedContentInfo(ObjectIdentifier contentType, byte[]? content)`

Initialises a new EncapsulatedContentInfo.

## Properties

### `ContentType`

```csharp
ObjectIdentifier ContentType
```

The content type OID (eContentType).

### `Content`

```csharp
byte[]? Content
```

The wrapped content bytes when present; null when this is a detached signature.

### `IsDetached`

```csharp
bool IsDetached => Content is null
```

True when this is a detached signature (no eContent).

## Methods

### `Read`

__static__

```csharp
static EncapsulatedContentInfo Read(Asn1Reader reader)
```

Reads an EncapsulatedContentInfo from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/EncapsulatedContentInfo.cs`](../../../src/Chuvadi.Cryptography/Cms/EncapsulatedContentInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
