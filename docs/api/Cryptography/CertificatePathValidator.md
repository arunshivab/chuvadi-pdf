# CertificatePathValidator

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

Validates X.509 certificate paths against a trust store, per RFC 5280 §6.1.

```csharp
public static class CertificatePathValidator
```

## Methods

### `Validate`

__static__

```csharp
static CertificatePathValidationResult Validate(IReadOnlyList<CertificatePath> paths, DateTimeOffset validationTime)
```

Validates each candidate path in turn and returns the first one that passes, or a failure result describing why no path was acceptable.

**Parameters**

- `paths` — Candidate paths (typically from `CertificatePathBuilder`).
- `validationTime` — The instant at which to check validity periods.

### `Validate`

__static__

```csharp
static CertificatePathValidationResult Validate(IReadOnlyList<CertificatePath> paths, DateTimeOffset validationTime, IReadOnlyList<CertificateList>? crls)
```

Validates each candidate path against the trust store and the supplied CRLs, returning the first one that passes.

**Parameters**

- `paths` — Candidate paths (typically from `CertificatePathBuilder`).
- `validationTime` — The instant at which to check validity periods.
- `crls` — Optional CRLs. When non-null, every certificate in the path is checked against any CRL whose issuer matches the certificate's issuer. Revocation is opportunistic: a cert without a covering CRL is accepted (soft-fail).

### `Validate`

__static__

```csharp
static CertificatePathValidationResult Validate(IReadOnlyList<CertificatePath> paths, DateTimeOffset validationTime, IReadOnlyList<CertificateList>? crls, IReadOnlyList<OcspResponse>? ocspResponses)
```

Validates each candidate path against the trust store and the supplied CRLs and OCSP responses, returning the first one that passes.

**Parameters**

- `paths` — Candidate paths.
- `validationTime` — The instant at which to check validity periods.
- `crls` — CRLs to consult, or null.
- `ocspResponses` — OCSP responses to consult, or null.

### `ValidatePath`

__static__

```csharp
static CertificatePathValidationResult ValidatePath(CertificatePath path, DateTimeOffset validationTime)
```

Validates a single path. Public for testability.

### `ValidatePath`

__static__

```csharp
static CertificatePathValidationResult ValidatePath(CertificatePath path, DateTimeOffset validationTime, IReadOnlyList<CertificateList>? crls)
```

Validates a single path with optional CRL revocation checking.

### `ValidatePath`

__static__

```csharp
static CertificatePathValidationResult ValidatePath(CertificatePath path, DateTimeOffset validationTime, IReadOnlyList<CertificateList>? crls, IReadOnlyList<OcspResponse>? ocspResponses)
```

Validates a single path with optional CRL and OCSP revocation checking.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePathValidator.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePathValidator.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
