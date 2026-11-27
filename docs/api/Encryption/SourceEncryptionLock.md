# SourceEncryptionLock

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Captures the encryption of a source document so a rewrite can keep it byte-for-byte: the same `/Encrypt` dictionary (and therefore the same owner and user passwords, permissions and key derivation) and the same file identifier. Obtain one from `PdfDocument.CaptureEncryptionLock()` and pass it to a save that preserves encryption.

```csharp
public sealed class SourceEncryptionLock
```

## Remarks

The raw file encryption key is held internally and is never exposed by any public member — the lock can re-apply the encryption but cannot hand the key out. A lock keeps a document's protection; it does not weaken it, so a consumer refuses to write it back with a legacy RC4 key length that is not a standard 128-bit key (see `KeyBits`).

## Properties

### `Algorithm`

```csharp
EncryptionAlgorithm Algorithm
```

Gets the source document's stream encryption algorithm.

### `KeyBits`

```csharp
int KeyBits
```

Gets the source document's file-key length in bits (the `/Length` entry).

---

_Source: [`src/Chuvadi.Pdf.Encryption/SourceEncryptionLock.cs`](../../../src/Chuvadi.Pdf.Encryption/SourceEncryptionLock.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
