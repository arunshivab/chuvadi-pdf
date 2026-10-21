# StandardSecurityHandler

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Standard security handler: derives a file encryption key from a user/owner password and the document's /Encrypt dictionary.

```csharp
public static class StandardSecurityHandler
```

## Methods

### `DeriveKeyR4`

__static__

```csharp
static byte[] DeriveKeyR4(string password, byte[] oValue, int permissions, byte[] firstFileId, int revision, int keyLength, bool encryptMetadata = true)
```

Computes the file encryption key for R≤4 from the user password.

**Parameters**

- `password` — UTF-8 / PDFDocEncoding password. Empty if none.
- `oValue` — /O entry from /Encrypt (32 bytes).
- `permissions` — /P entry from /Encrypt.
- `firstFileId` — First element of trailer /ID (16 bytes).
- `revision` — /R entry: 2, 3, or 4.
- `keyLength` — /Length entry in bytes (5 for R=2, 16 for R≥3).
- `encryptMetadata` — /EncryptMetadata entry (default true).

**Returns:** The derived file encryption key.

### `ValidateUserPasswordR4`

__static__

```csharp
static bool ValidateUserPasswordR4(byte[] derivedKey, byte[] uValue, byte[] firstFileId, int revision)
```

Verifies a user password by comparing the computed U value against /U.

**Returns:** true if the password is the user password.

### `ValidatePasswordR6`

__static__

```csharp
static byte[]? ValidatePasswordR6(string password, byte[] uValue, // 48 bytes: 32-byte hash, 8-byte validation salt, 8-byte key salt byte[] ueValue, // 32 bytes: encrypted file key byte[] oValue, byte[] oeValue, bool tryOwnerPassword)
```

Validates a password against an R=6 /U value (ISO 32000-2 Algorithm 2.B). Returns the derived file encryption key on success, or null on failure.

---

_Source: [`src/Chuvadi.Pdf.Encryption/StandardSecurityHandler.cs`](../../../src/Chuvadi.Pdf.Encryption/StandardSecurityHandler.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
