# TimeStampStatus

**Enum** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

Status code from a TSA response per RFC 3161 §2.4.2 (PKIStatus).

```csharp
public enum TimeStampStatus
```

## Values

| Name | Description |
|---|---|
| `Granted` | Granted — timestamp produced as requested. |
| `GrantedWithMods` | Granted with modifications — TSA chose different parameters but produced a token. |
| `Rejection` | Rejection — request refused. |
| `Waiting` | Waiting — TSA needs more time (not used for synchronous TSP). |
| `RevocationWarning` | Revocation warning. |
| `RevocationNotification` | Revocation notification. |

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampResponse.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampResponse.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
