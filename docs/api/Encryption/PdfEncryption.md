# PdfEncryption

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Top-level helper for decrypting an encrypted PDF.

```csharp
public static class PdfEncryption
```

## Methods

### `TryOpen`

__static__

```csharp
static Decryptor? TryOpen(PdfDictionary encryptDict, byte[] firstFileId, string password)
```

Builds a `Decryptor` for the given trailer's /Encrypt entry and /ID array, using the given password.

**Parameters**

- `encryptDict` — The trailer's /Encrypt dictionary, resolved.
- `firstFileId` — First element of the trailer /ID array (16 bytes).
- `password` — Empty for unencrypted-or-default; user/owner password otherwise.

**Returns:** A configured Decryptor, or null if the password is wrong.

---

_Source: [`src/Chuvadi.Pdf.Encryption/PdfEncryption.cs`](../../../src/Chuvadi.Pdf.Encryption/PdfEncryption.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
