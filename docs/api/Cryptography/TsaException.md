# TsaException

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

Thrown when a TSA returns a non-success HTTP status or otherwise fails to produce a usable response.

```csharp
public sealed class TsaException : Exception
```

## Constructors

### `TsaException(string message) : base(message)`

Initialises a new exception with the given message.

### `TsaException(string message, Exception innerException)`

Initialises a new exception with a message and inner cause.

### `TsaException() : base("A TSA error occurred.")`

Initialises a new exception with the default message.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/HttpTsaClient.cs`](../../../src/Chuvadi.Cryptography/Timestamps/HttpTsaClient.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
