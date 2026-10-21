# CertificatePathBuilder

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

Builds candidate certificate paths from a leaf certificate to a trust anchor.

```csharp
public static class CertificatePathBuilder
```

## Remarks

Walks the issuer chain from leaf upward, using name chaining (issuer DN == subject DN on the next cert up). Multiple valid paths can exist when a CA has been cross-signed; `BuildPaths` returns all of them so a downstream validator can try each.

## Methods

### `BuildPaths`

__static__

```csharp
static IReadOnlyList<CertificatePath> BuildPaths(X509Certificate leaf, IEnumerable<X509Certificate> intermediates, TrustStore trustStore, int maxPathLength = DefaultMaxPathLength)
```

Returns all candidate paths from `leaf` to any trust anchor in `trustStore`, using `intermediates` for any links between leaf and anchor.

**Parameters**

- `leaf` — The end-entity certificate to start from.
- `intermediates` — Candidate intermediate CA certificates to link with.
- `trustStore` — The trust anchors to terminate paths at.
- `maxPathLength` — Safety cap on path depth (defaults to 16). Pathological inputs with cycles or huge fan-out are bounded.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePathBuilder.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePathBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
