# SignerIdentifierKind

**Enum** in `Chuvadi.Cryptography.Cms` (Cryptography)

The two variants of a SignerIdentifier.

```csharp
public enum SignerIdentifierKind
```

## Values

| Name | Description |
|---|---|
| `IssuerAndSerial` | issuerAndSerialNumber — by issuer DN and certificate serial. |
| `SubjectKeyIdentifier` | subjectKeyIdentifier [0] — by SubjectKeyIdentifier from the cert's extension. |

---

_Source: [`src/Chuvadi.Cryptography/Cms/SignerIdentifier.cs`](../../../src/Chuvadi.Cryptography/Cms/SignerIdentifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
