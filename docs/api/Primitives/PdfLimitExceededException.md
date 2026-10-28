# PdfLimitExceededException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when an operation would go past one of the `PdfLimits` in effect (input size, page count, memory, decoded stream size, page complexity or output pixels). The work is abandoned; nothing partial is returned.

```csharp
public sealed class PdfLimitExceededException : PdfException
```

## Constructors

### `PdfLimitExceededException()`

Initialises a new instance with no message.

### `PdfLimitExceededException(string message)`

Initialises a new instance with the given message.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfLimitExceededException(string message, Exception innerException)`

Initialises a new instance with the given message and inner exception.

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

### `PdfLimitExceededException(string limitName, long limit, long actual)`

Initialises a new instance for a named limit.

**Parameters**

- `limitName` — The `PdfLimits` property that was exceeded.
- `limit` — The configured limit.
- `actual` — The value that went past it (at least this much).

## Properties

### `LimitName`

```csharp
string LimitName
```

Gets the name of the `PdfLimits` property that was exceeded.

### `Limit`

```csharp
long Limit
```

Gets the configured limit.

### `Actual`

```csharp
long Actual
```

Gets the value that went past the limit (at least this much).

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfLimitExceededException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfLimitExceededException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
