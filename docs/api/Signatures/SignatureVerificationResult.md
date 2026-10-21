# SignatureVerificationResult

**Class** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

The result of verifying a PDF digital signature.

```csharp
public sealed class SignatureVerificationResult
```

## Constructors

### `SignatureVerificationResult(SignatureVerificationStatus status, string message, X509Certificate? signerCertificate, bool integrityVerified, bool trustValidated = false, CertificatePath? validatedPath = null, bool timestampValidated = false, DateTimeOffset? signatureTimestamp = null, X509Certificate? timestampCertificate = null, bool timestampTrustValidated = false, CertificatePath? timestampValidatedPath = null)`

Initialises a new result.

## Properties

### `Status`

```csharp
SignatureVerificationStatus Status
```

The overall outcome.

### `Message`

```csharp
string Message
```

A human-readable explanation of the result.

### `SignerCertificate`

```csharp
X509Certificate? SignerCertificate
```

The signer's certificate, when located inside the CMS envelope.

### `IntegrityVerified`

```csharp
bool IntegrityVerified
```

True iff the cryptographic signature checks out AND the signed bytes' digest matches the messageDigest signed attribute. This is the strict cryptographic answer regardless of whether the signer is to be believed.

### `TrustValidated`

```csharp
bool TrustValidated
```

True iff `IntegrityVerified` is true AND the signer's certificate chain validates to a configured trust anchor per RFC 5280 §6.1. False when no trust store was supplied or path validation failed.

### `ValidatedPath`

```csharp
CertificatePath? ValidatedPath
```

The certificate path that validated against the trust store, when `TrustValidated` is true.

### `TimestampValidated`

```csharp
bool TimestampValidated
```

True iff a signature timestamp (RFC 3161) was present in the CMS unsigned attributes, decoded cleanly, signature-verified, and its messageImprint matched the SignerInfo signature bytes.

### `SignatureTimestamp`

```csharp
DateTimeOffset? SignatureTimestamp
```

When a signature timestamp was present, the genTime claimed by the TSA. Even when `TimestampValidated` is false (e.g. the TST was present but did not verify), this carries the genTime declared by the token for debugging.

### `TimestampCertificate`

```csharp
X509Certificate? TimestampCertificate
```

The TSA's signing certificate, when a timestamp was found.

### `TimestampTrustValidated`

```csharp
bool TimestampTrustValidated
```

True iff the TSA's certificate chain was successfully path-validated against the supplied `TsaTrustStore` at the timestamp's genTime. Always false when no `TsaTrustStore` was supplied. Independent of `TimestampValidated`, which only reports the cryptographic verification.

### `TimestampValidatedPath`

```csharp
CertificatePath? TimestampValidatedPath
```

The validated path for the TSA's certificate, when `TimestampTrustValidated` is true. Null otherwise.

### `IsValid`

```csharp
bool IsValid => Status == SignatureVerificationStatus.Valid
```

Convenience shorthand for `Status == Valid`.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
