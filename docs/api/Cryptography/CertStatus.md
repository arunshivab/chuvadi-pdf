# CertStatus

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

The OCSP responder's verdict on one certificate.

```csharp
public sealed class CertStatus
```

## Remarks

RFC 6960 §4.2.1: 
```
 CertStatus ::= CHOICE { good     [0] IMPLICIT NULL, revoked  [1] IMPLICIT RevokedInfo, unknown  [2] IMPLICIT UnknownInfo } RevokedInfo ::= SEQUENCE { revocationTime  GeneralizedTime, revocationReason [0] EXPLICIT CRLReason OPTIONAL } 
```

## Properties

### `Kind`

```csharp
CertStatusKind Kind
```

The status kind.

### `RevocationTime`

```csharp
DateTimeOffset? RevocationTime
```

When status is `CertStatusKind.Revoked`, the time of revocation.

### `RevocationReason`

```csharp
CrlReason RevocationReason
```

When status is `CertStatusKind.Revoked`, the reason if reported.

### `IsGood`

```csharp
bool IsGood => Kind == CertStatusKind.Good
```

Convenience: true iff the responder said this cert is OK.

### `IsRevoked`

```csharp
bool IsRevoked => Kind == CertStatusKind.Revoked
```

Convenience: true iff the responder said this cert is revoked.

### `IsUnknown`

```csharp
bool IsUnknown => Kind == CertStatusKind.Unknown
```

Convenience: true iff the responder reported it doesn't know about this cert.

## Methods

### `Good`

__static__

```csharp
static CertStatus Good() => new(CertStatusKind.Good, null, CrlReason.Unspecified)
```

Factory: the responder says this certificate is good.

### `Revoked`

__static__

```csharp
static CertStatus Revoked(DateTimeOffset revokedAt, CrlReason reason)
```

Factory: the responder says this certificate is revoked.

### `Unknown`

__static__

```csharp
static CertStatus Unknown() => new(CertStatusKind.Unknown, null, CrlReason.Unspecified)
```

Factory: the responder does not know about this certificate.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/CertStatus.cs`](../../../src/Chuvadi.Cryptography/Ocsp/CertStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
