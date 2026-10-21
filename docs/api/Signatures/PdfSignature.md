# PdfSignature

**Class** in `Chuvadi.Pdf.Signatures` (Signatures)

One digital signature found in a PDF document.

```csharp
public sealed class PdfSignature
```

## Remarks

A PDF signature is the combination of a signature dictionary and the AcroForm field that points to it. PDF 32000-1 §12.8.1 lays out the signature dictionary entries: 
 
- /Type — must be /Sig (or /DocTimeStamp for document timestamps). 
- /Filter — the preferred handler, typically /Adobe.PPKLite. 
- /SubFilter — encoding of the /Contents value; see `SignatureSubFilter`. 
- /ByteRange — the two regions of the file the signature covers. 
- /Contents — the cryptographic envelope itself (CMS / PKCS#7 SignedData for the common SubFilter values). 
- /M — signing time (PDF date string; optional and often unreliable — the authoritative signing time, when present, lives inside the CMS signed attributes). 
- /Name, /Reason, /Location, /ContactInfo — optional signer metadata.

## Constructors

### `PdfSignature(string fieldName, string? filter, string? subFilter, ByteRange byteRange, byte[] contents, string? name, string? reason, string? location, string? contactInfo, DateTimeOffset? signingTimeFromDictionary, bool isDocumentTimestamp)`

Initialises a new PdfSignature.

## Properties

### `FieldName`

```csharp
string FieldName
```

The AcroForm field name that holds this signature (the /T entry).

### `Filter`

```csharp
string? Filter
```

The /Filter entry — preferred signature handler.

### `SubFilter`

```csharp
string? SubFilter
```

The /SubFilter entry — encoding of `Contents`.

### `ByteRange`

```csharp
ByteRange ByteRange
```

The /ByteRange covering the signed regions.

### `Contents`

```csharp
byte[] Contents
```

The /Contents bytes — the cryptographic envelope.

### `Name`

```csharp
string? Name
```

The /Name entry — declared signer name.

### `Reason`

```csharp
string? Reason
```

The /Reason entry — declared reason for signing.

### `Location`

```csharp
string? Location
```

The /Location entry — declared location.

### `ContactInfo`

```csharp
string? ContactInfo
```

The /ContactInfo entry.

### `SigningTimeFromDictionary`

```csharp
DateTimeOffset? SigningTimeFromDictionary
```

The /M entry parsed as a date, or null when absent or unparseable.

### `IsDocumentTimestamp`

```csharp
bool IsDocumentTimestamp
```

True when this is a document timestamp (/Type /DocTimeStamp), not a signature.

## Methods

### `SignatureSubFilter.IsCmsBased`

```csharp
bool IsCmsBased => SignatureSubFilter.IsCmsBased(SubFilter ?? string.Empty)
```

True when the /SubFilter indicates the /Contents bytes are a CMS / PKCS#7 SignedData container that can be parsed by `DecodeCms`.

### `DecodeCms`

```csharp
SignedData DecodeCms()
```

Decodes `Contents` as a CMS / PKCS#7 SignedData container. Throws if `SubFilter` is not CMS-based.

---

_Source: [`src/Chuvadi.Pdf.Signatures/PdfSignature.cs`](../../../src/Chuvadi.Pdf.Signatures/PdfSignature.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
