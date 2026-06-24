# OcspResponseStatus

**Enum** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

The top-level status of an OCSP response.

```csharp
public enum OcspResponseStatus
```

## Values

| Name | Description |
|---|---|
| `Successful` | Response has valid confirmations. |
| `MalformedRequest` | Illegal confirmation request. |
| `InternalError` | Internal error in issuer. |
| `TryLater` | Try again later. |
| `SigRequired` | Must sign the request. |
| `Unauthorized` | Request unauthorised. |

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/OcspResponseStatus.cs`](../../../src/Chuvadi.Cryptography/Ocsp/OcspResponseStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
