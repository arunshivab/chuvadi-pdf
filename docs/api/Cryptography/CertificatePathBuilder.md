# CertificatePathBuilder

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

Builds candidate certificate paths from a leaf certificate to a trust anchor.

```csharp
public static class CertificatePathBuilder
```

## Remarks

Walks the issuer chain from leaf upward, using name chaining (issuer DN == subject DN on the next cert up). Multiple valid paths can exist when a CA has been cross-signed; `BuildPaths` returns all of them so a downstream validator can try each.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/CertificatePathBuilder.cs`](../../../src/Chuvadi.Cryptography/PathValidation/CertificatePathBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
