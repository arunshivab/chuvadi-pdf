# EncryptionInfo

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Describes the encryption properties of a `PdfDocument`.

```csharp
public sealed class EncryptionInfo
```

## Remarks

Returned from `PdfDocument.Encryption` when the document is encrypted; `null` when the document has no `/Encrypt` entry in its trailer. The properties on this type expose what the document declares about its security handler — they do not perform any cryptographic operations themselves.  

 The permission decoder properties (`AllowPrint`, `AllowModify`, etc.) interpret the `Permissions` bit mask per PDF 32000-1:2008 §7.6.3.2, Table 22. These flags are advisory only: a viewer is free to honour or ignore them, and an owner password bypasses them entirely.

## Constructors

### `EncryptionInfo(EncryptionAlgorithm algorithm, int keyLength, int revision, int version, int permissions, bool encryptMetadata)`

Initialises an `EncryptionInfo`.

**Parameters**

- `algorithm` — Algorithm in use.
- `keyLength` — Key length in bytes.
- `revision` — PDF /R revision (2..6).
- `version` — PDF /V version (1..5).
- `permissions` — PDF /P permission bit mask.
- `encryptMetadata` — Whether /Metadata streams are encrypted.

## Properties

### `Algorithm`

```csharp
EncryptionAlgorithm Algorithm
```

Gets the encryption algorithm in use.

### `KeyLength`

```csharp
int KeyLength
```

Gets the key length in bytes (5 for RC4-40, 16 for AES-128, 32 for AES-256).

### `Revision`

```csharp
int Revision
```

Gets the /R revision value (2..6).

### `Version`

```csharp
int Version
```

Gets the /V version value (1..5).

### `Permissions`

```csharp
int Permissions
```

Gets the raw /P permission bit mask.

### `EncryptMetadata`

```csharp
bool EncryptMetadata
```

Gets whether the /Metadata stream is encrypted.

## Methods

### `=>`

```csharp
bool AllowPrint => (Permissions & PrintBit) != 0
```

Bit 3 — Print the document (possibly at low quality).

### `=>`

```csharp
bool AllowModify => (Permissions & ModifyBit) != 0
```

Bit 4 — Modify the contents of the document.

### `=>`

```csharp
bool AllowCopy => (Permissions & CopyBit) != 0
```

Bit 5 — Copy or extract text and graphics from the document.

### `=>`

```csharp
bool AllowAnnotate => (Permissions & AnnotateBit) != 0
```

Bit 6 — Add or modify text annotations and fill in interactive form fields.

### `=>`

```csharp
bool AllowFillForms => (Permissions & FillFormsBit) != 0
```

Bit 9 — Fill in existing interactive form fields (R≥3).

### `=>`

```csharp
bool AllowAccessibilityExtract => (Permissions & AccessibilityBit) != 0
```

Bit 10 — Extract text and graphics for accessibility (R≥3, deprecated in PDF 2.0).

### `=>`

```csharp
bool AllowAssemble => (Permissions & AssembleBit) != 0
```

Bit 11 — Assemble the document: insert, rotate, or delete pages (R≥3).

### `=>`

```csharp
bool AllowPrintHighQuality => (Permissions & PrintHighQualityBit) != 0
```

Bit 12 — Print the document at high quality (R≥3).

---

_Source: [`src/Chuvadi.Pdf.Documents/EncryptionInfo.cs`](../../../src/Chuvadi.Pdf.Documents/EncryptionInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
