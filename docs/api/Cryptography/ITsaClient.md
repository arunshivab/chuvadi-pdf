# ITsaClient

**Interface** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

A client capable of fetching an RFC 3161 timestamp from a TSA.

```csharp
public interface ITsaClient
```

## Remarks

The abstraction lets callers plug in any transport: the supplied `HttpTsaClient` uses HTTP/HTTPS; in-memory mocks for tests or alternative transports (e.g. authenticated channels for private TSAs) implement this interface and can be passed wherever a TSA timestamp is required.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/ITsaClient.cs`](../../../src/Chuvadi.Cryptography/Timestamps/ITsaClient.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
