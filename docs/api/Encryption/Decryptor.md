# Decryptor

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Decrypts individual strings and streams in an encrypted PDF.

```csharp
public sealed class Decryptor
```

## Remarks

For R≤4 the per-object key is derived from the file key plus the indirect object's number and generation per PDF Algorithm 1. AES-128 uses a slightly extended key (with the "sAlT" salt suffix per §7.6.2). For R=6 / AES-256 the file key is used directly without per-object derivation.

## Constructors

### `Decryptor(byte[] fileKey, EncryptionAlgorithm algorithm)`

Constructs a decryptor for the given file key and algorithm.

## Properties

### `OpenedWith`

```csharp
PdfPasswordKind OpenedWith
```

Gets which password opened the document (3.32.0); set by `PdfEncryption.TryOpen`. A decryptor built directly from a file key reports `PdfPasswordKind.NoPassword`.

## Methods

### `Decrypt`

```csharp
byte[] Decrypt(byte[] data, int objectNumber, int generation)
```

Decrypts data belonging to a specific indirect object.

**Parameters**

- `data` — Encrypted bytes (string contents or stream payload).
- `objectNumber` — /N field of the indirect object.
- `generation` — /G field of the indirect object.

---

_Source: [`src/Chuvadi.Pdf.Encryption/Decryptor.cs`](../../../src/Chuvadi.Pdf.Encryption/Decryptor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
