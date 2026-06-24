# RevokedCertificate

**Class** in `Chuvadi.Cryptography.Revocation` (Cryptography)

One revocation entry from a CRL.

```csharp
public sealed class RevokedCertificate
```

## Remarks

RFC 5280 §5.1: 
```
 revokedCertificates SEQUENCE OF SEQUENCE { userCertificate     CertificateSerialNumber, revocationDate      Time, crlEntryExtensions  Extensions OPTIONAL } OPTIONAL 
```

## Properties

### `UserCertificateSerial`

```csharp
BigInteger UserCertificateSerial
```

The revoked certificate's serial number.

### `RevocationDate`

```csharp
DateTimeOffset RevocationDate
```

The time the certificate was revoked.

### `Reason`

```csharp
CrlReason Reason
```

The revocation reason (Unspecified when the extension is absent).

### `InvalidityDate`

```csharp
DateTimeOffset? InvalidityDate
```

The invalidity date from the per-entry `invalidityDate` extension (RFC 5280 §5.3.2), when present. May predate `RevocationDate` for revocations issued after the key was suspected compromised.

---

_Source: [`src/Chuvadi.Cryptography/Revocation/RevokedCertificate.cs`](../../../src/Chuvadi.Cryptography/Revocation/RevokedCertificate.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
