# CertificatePathValidationResult

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

The result of running the path-validation algorithm against one or more candidate paths.

```csharp
public sealed class CertificatePathValidationResult
```

## Properties

### `Status`

```csharp
CertificatePathValidationStatus Status
```

The validation outcome.

### `Message`

```csharp
string Message
```

Human-readable explanation.

### `ValidatedPath`

```csharp
CertificatePath? ValidatedPath
```

The path that validated cleanly, when `IsValid` is true.

### `IsValid`

```csharp
bool IsValid => Status == CertificatePathValidationStatus.Valid
```

Convenience: true when `Status` is Valid.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePathValidationResult.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePathValidationResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
