# TimeStampVerificationResult

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

The result of verifying a TimeStampToken.

```csharp
public sealed class TimeStampVerificationResult
```

## Properties

### `Status`

```csharp
TimeStampVerificationStatus Status
```

The outcome.

### `Message`

```csharp
string Message
```

Human-readable explanation.

### `SignerCertificate`

```csharp
X509Certificate? SignerCertificate
```

The TSA's signing certificate, when located inside the token.

### `Timestamp`

```csharp
DateTimeOffset? Timestamp
```

The genTime claimed by the TSA, when the token parsed successfully.

### `IsValid`

```csharp
bool IsValid => Status == TimeStampVerificationStatus.Valid
```

Convenience: true iff `Status` is Valid.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampTokenVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
