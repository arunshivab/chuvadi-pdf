# ContentInfo

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

The outermost CMS structure — a tagged container that says "the following bytes are of contentType X."

```csharp
public sealed class ContentInfo
```

## Remarks

Structure: 
```
 ContentInfo ::= SEQUENCE { contentType  ContentType, content      [0] EXPLICIT ANY DEFINED BY contentType } ContentType ::= OBJECT IDENTIFIER 
```
 Inside a PDF signature dictionary, the bytes at /Contents always form one ContentInfo whose contentType is `id-signedData` (1.2.840.113549.1.7.2).

## Constructors

### `ContentInfo(ObjectIdentifier contentType, byte[] contentEncoded)`

Initialises a new ContentInfo.

## Properties

### `ContentType`

```csharp
ObjectIdentifier ContentType
```

The content type OID.

### `ContentEncoded`

```csharp
byte[] ContentEncoded
```

The complete encoded TLV bytes of the inner content.

## Methods

### `GetSignedData`

```csharp
SignedData GetSignedData()
```

Decodes the inner content as a SignedData. Throws when this ContentInfo does not carry a SignedData.

### `Read`

__static__

```csharp
static ContentInfo Read(Asn1Reader reader)
```

Reads a ContentInfo from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/ContentInfo.cs`](../../../src/Chuvadi.Cryptography/Cms/ContentInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
