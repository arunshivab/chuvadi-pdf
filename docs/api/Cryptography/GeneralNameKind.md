# GeneralNameKind

**Enum** in `Chuvadi.Cryptography.X509` (Cryptography)

The variant types within a GeneralName CHOICE.

```csharp
public enum GeneralNameKind
```

## Values

| Name | Description |
|---|---|
| `OtherName` | otherName [0] — OtherName SEQUENCE. |
| `Rfc822Name` | rfc822Name [1] — IA5String. |
| `DnsName` | dNSName [2] — IA5String. |
| `X400Address` | x400Address [3] — ORAddress (raw). |
| `DirectoryName` | directoryName [4] — Name. |
| `EdiPartyName` | ediPartyName [5] — EDIPartyName (raw). |
| `UniformResourceIdentifier` | uniformResourceIdentifier [6] — IA5String. |
| `IpAddress` | iPAddress [7] — OCTET STRING. |
| `RegisteredId` | registeredID [8] — OBJECT IDENTIFIER. |

---

_Source: [`src/Chuvadi.Cryptography/X509/GeneralName.cs`](../../../src/Chuvadi.Cryptography/X509/GeneralName.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
