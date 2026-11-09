# EncryptionOptions

**Class** in `Chuvadi.Pdf.IO` (IO)

Options that drive encrypted PDF writing.

```csharp
public sealed class EncryptionOptions
```

## Remarks

Only AES-128 and AES-256 are supported for writing. Legacy RC4 is read-only. Construct an instance for the chosen algorithm via the static factories.

## Properties

### `Algorithm`

```csharp
EncryptionAlgorithm Algorithm
```

Gets the chosen encryption algorithm.

### `FileKey`

```csharp
byte[] FileKey
```

Gets the file encryption key. Random unless overridden.

### `UserPassword`

```csharp
string UserPassword
```

Gets the user password used to derive the U/UE entries.

### `OwnerPassword`

```csharp
string OwnerPassword
```

Gets the owner password used to derive the O/OE entries.

### `Permissions`

```csharp
int Permissions
```

Gets or initialises the permission bit mask written to /P. Defaults to `AllPermissionsAllowed` (every action permitted). Set a narrower value to restrict what user-password holders may do.

### `EncryptMetadata`

```csharp
bool EncryptMetadata
```

Gets or initialises whether /Metadata streams should be encrypted. Default: true. Setting false matches /EncryptMetadata=false in the spec.

## Methods

### `PermissionMask`

__static__

```csharp
static int PermissionMask(PdfPermissions allowed)
```

Returns the `/P` value that grants exactly `allowed`, for `Permissions` (3.32.0, D-230). `WithPermissions` sets it for you.

**Parameters**

- `allowed` — The actions a user-password holder may take.

**Returns:** The permission mask: the bits for the allowed actions set, the other permission bits clear, bits 1 and 2 clear and every reserved bit set, as PDF 32000-1:2008 Table 22 requires. `PdfPermissions.All` gives `AllPermissionsAllowed`.

**Remarks:** `PdfPermissions.PrintHighQuality` also sets the print bit, because the specification gives bit 12 meaning only together with bit 3.

### `Aes128`

__static__

```csharp
static EncryptionOptions Aes128(string userPassword, string? ownerPassword = null)
```

Creates options for AES-128 encryption (V=4, R=4, AESV2 crypt filter). Generates a random 16-byte file key.

### `Aes256`

__static__

```csharp
static EncryptionOptions Aes256(string userPassword, string? ownerPassword = null)
```

Creates options for AES-256 encryption (V=5, R=6, ISO 32000-2 standardised). Generates a random 32-byte file key.

## Fields

### `AllPermissionsAllowed`

```csharp
const int AllPermissionsAllowed = -4
```

The /P permission mask that grants every action defined by PDF 32000-1:2008 §7.6.3.2, Table 22 (print, modify, copy, annotate, fill forms, accessibility extract, assemble, high-quality print).

---

_Source: [`src/Chuvadi.Pdf.IO/EncryptionOptions.cs`](../../../src/Chuvadi.Pdf.IO/EncryptionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
