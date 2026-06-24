# IAsyncTsaClient

**Interface** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

An asynchronous TSA client. Same contract as `ITsaClient` but exposing an async fetch method, useful when signing happens on a thread that should not block on network I/O.

```csharp
public interface IAsyncTsaClient
```

## Remarks

Implementations that wrap real network transports (HTTP, etc.) should implement this in preference to `ITsaClient`; Chuvadi's `HttpTsaClient` implements both so callers can choose either style at their seam.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/IAsyncTsaClient.cs`](../../../src/Chuvadi.Cryptography/Timestamps/IAsyncTsaClient.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
