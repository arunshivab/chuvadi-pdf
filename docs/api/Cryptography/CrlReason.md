# CrlReason

**Enum** in `Chuvadi.Cryptography.Revocation` (Cryptography)

The reason a certificate was revoked, as encoded in the per-entry `reasonCode` CRL extension (OID 2.5.29.21).

```csharp
public enum CrlReason
```

## Remarks

RFC 5280 §5.3.1 reserves value 7. When the `reasonCode` extension is absent, RFC 5280 says the reason is unspecified; Chuvadi exposes that as `Unspecified`.

## Values

| Name | Description |
|---|---|
| `Unspecified` | No reason given (extension absent or value 0). |
| `KeyCompromise` | Subject's private key has been compromised (value 1). |
| `CaCompromise` | Issuing CA's private key has been compromised (value 2). |
| `AffiliationChanged` | Subject has changed affiliation (value 3). |
| `Superseded` | Certificate has been superseded (value 4). |
| `CessationOfOperation` | Subject is no longer operational (value 5). |
| `CertificateHold` | Certificate is on hold (value 6, reversible). |
| `RemoveFromCrl` | Certificate previously on hold is removed from CRL (value 8). |
| `PrivilegeWithdrawn` | Certificate's privileges have been withdrawn (value 9). |
| `AaCompromise` | Attribute authority compromise (value 10). |

---

_Source: [`src/Chuvadi.Cryptography/Revocation/CrlReason.cs`](../../../src/Chuvadi.Cryptography/Revocation/CrlReason.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
