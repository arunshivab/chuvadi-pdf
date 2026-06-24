# KeyUsageFlags

**Enum** in `Chuvadi.Cryptography.X509` (Cryptography)

```csharp
public enum KeyUsageFlags
```

## Values

| Name | Description |
|---|---|
| `None` | No usage permitted. |
| `DigitalSignature` | digitalSignature (bit 0). |
| `NonRepudiation` | nonRepudiation / contentCommitment (bit 1). |
| `KeyEncipherment` | keyEncipherment (bit 2). |
| `DataEncipherment` | dataEncipherment (bit 3). |
| `KeyAgreement` | keyAgreement (bit 4). |
| `KeyCertSign` | keyCertSign (bit 5). |
| `CrlSign` | cRLSign (bit 6). |
| `EncipherOnly` | encipherOnly (bit 7). |
| `DecipherOnly` | decipherOnly (bit 8). |

---

_Source: [`src/Chuvadi.Cryptography/X509/KeyUsageExtension.cs`](../../../src/Chuvadi.Cryptography/X509/KeyUsageExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
