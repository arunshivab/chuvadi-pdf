# CertStatusKind

**Enum** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

The three possible `CertStatus.Kind` values.

```csharp
public enum CertStatusKind
```

## Values

| Name | Description |
|---|---|
| `Good` | The certificate is valid per the responder. |
| `Revoked` | The certificate has been revoked. |
| `Unknown` | The responder does not know about this certificate. |

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/CertStatus.cs`](../../../src/Chuvadi.Cryptography/Ocsp/CertStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
