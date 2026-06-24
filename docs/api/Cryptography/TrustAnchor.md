# TrustAnchor

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

A trust anchor — a CA the verifier trusts to vouch for certificates it issues.

```csharp
public sealed class TrustAnchor
```

## Remarks

RFC 5280 §6.1.1(d) defines a trust anchor as a (trusted CA name, trusted CA public key) pair, optionally with initial path-validation constraints. In practice, most consumers carry trust anchors as full certificates (typically self-signed roots from a system or curated trust store). Chuvadi supports both representations.

## Constructors

### `TrustAnchor(X509Certificate certificate)`

Builds a trust anchor from a full trusted certificate.

### `TrustAnchor(X509Name subject, SubjectPublicKeyInfo subjectPublicKeyInfo)`

Builds a trust anchor from a name + key pair (no full certificate).

## Properties

### `Subject`

```csharp
X509Name Subject
```

The trusted CA's distinguished name.

### `SubjectPublicKeyInfo`

```csharp
SubjectPublicKeyInfo SubjectPublicKeyInfo
```

The trusted CA's public key (the algorithm and key bits).

### `Certificate`

```csharp
X509Certificate? Certificate
```

The full certificate, when this trust anchor was built from one. May be null when the anchor was constructed from name + key only.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/TrustAnchor.cs`](../../../src/Chuvadi.Cryptography/PathValidation/TrustAnchor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
