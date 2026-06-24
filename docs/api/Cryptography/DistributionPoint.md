# DistributionPoint

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

One distribution point inside a CRLDistributionPoints extension.

```csharp
public sealed class DistributionPoint
```

## Constructors

### `DistributionPoint(IList<GeneralName> fullName)`

Initialises a new DistributionPoint.

## Properties

### `FullName`

```csharp
ReadOnlyCollection<GeneralName> FullName
```

The full names of the CRL distribution endpoint(s).

---

_Source: [`src/Chuvadi.Cryptography/X509/CrlDistributionPointsExtension.cs`](../../../src/Chuvadi.Cryptography/X509/CrlDistributionPointsExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
