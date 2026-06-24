# CertificatePath

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

A certificate path: an ordered sequence from the end-entity (leaf) to a trust anchor, plus the matching anchor.

```csharp
public sealed class CertificatePath
```

## Remarks

Convention: `Certificates`[0] is the leaf, the last element is the certificate issued by `Anchor`. The trust anchor's certificate (when present) is NOT included in `Certificates` — the anchor's public key is consumed to verify the last certificate, but the anchor itself is not part of the path being validated.

## Constructors

### `CertificatePath(IList<X509Certificate> certificates, TrustAnchor anchor)`

Initialises a new CertificatePath.

## Properties

### `Certificates`

```csharp
IReadOnlyList<X509Certificate> Certificates
```

The certificates in the path, leaf first, intermediate-CA-issued-by-anchor last.

### `Anchor`

```csharp
TrustAnchor Anchor
```

The trust anchor that issued the topmost certificate.

### `Leaf`

```csharp
X509Certificate Leaf => Certificates[0]
```

The leaf certificate (end entity).

### `Length`

```csharp
int Length => Certificates.Count
```

The number of certificates in the path (excluding the anchor).

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePath.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePath.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
