# PdfPasswordRequiredException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when an encrypted PDF cannot be opened without a password: none was given and the empty user password does not open it, or the password given is neither the user nor the owner password.

```csharp
public sealed class PdfPasswordRequiredException : PdfParseException
```

## Remarks

Derives from `PdfParseException`, which open methods threw for this case before 3.24, so existing `catch (PdfParseException)` blocks still catch it. Check `PasswordSupplied` to tell "ask the user for a password" from "the password was wrong". To find out before opening, use `PdfDocument.RequiresPassword`.

## Constructors

### `PdfPasswordRequiredException()`

Initialises a new instance for a document opened without a password.

### `PdfPasswordRequiredException(string message)`

Initialises a new instance with the given message.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfPasswordRequiredException(string message, Exception innerException)`

Initialises a new instance with the given message and inner exception.

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

### `PdfPasswordRequiredException(bool passwordSupplied)`

Initialises a new instance.

**Parameters**

- `passwordSupplied` — True when a non-empty password was given and rejected; false when none was given (or the empty password was tried).

## Properties

### `PasswordSupplied`

```csharp
bool PasswordSupplied
```

Gets a value indicating whether a non-empty password was given and rejected (true), or no password was given (false).

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPasswordRequiredException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPasswordRequiredException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
