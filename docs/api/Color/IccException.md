# IccException

**Class** in `Chuvadi.Pdf.Color` (Color)

Thrown when an ICC profile is malformed or unsupported.

```csharp
public sealed class IccException : Exception
```

## Constructors

### `IccException()`

Initialises an empty `IccException`.

### `IccException(string message) : base(message)`

Initialises an `IccException` with a message.

### `IccException(string message, Exception inner) : base(message, inner)`

Initialises an `IccException` with a message and inner exception.

---

_Source: [`src/Chuvadi.Pdf.Color/IccProfile.cs`](../../../src/Chuvadi.Pdf.Color/IccProfile.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
