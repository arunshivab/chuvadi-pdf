# CrlDistributionPointsExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The CRL Distribution Points extension — locations from which the issuer's Certificate Revocation List may be retrieved.

```csharp
public sealed class CrlDistributionPointsExtension
```

## Remarks

Structure (simplified — Chuvadi tracks only the fullName variant which covers virtually all real-world certificates): 
```
 CRLDistributionPoints ::= SEQUENCE SIZE (1..MAX) OF DistributionPoint DistributionPoint ::= SEQUENCE { distributionPoint [0] DistributionPointName OPTIONAL, reasons           [1] ReasonFlags OPTIONAL, cRLIssuer         [2] GeneralNames OPTIONAL } DistributionPointName ::= CHOICE { fullName                [0] GeneralNames, nameRelativeToCRLIssuer [1] RelativeDistinguishedName } 
```

## Constructors

### `CrlDistributionPointsExtension(IList<DistributionPoint> points)`

Initialises a new CrlDistributionPointsExtension.

## Properties

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.CrlDistributionPoints
```

The OID identifying this extension.

## Methods

### `new`

```csharp
ReadOnlyCollection<DistributionPoint> Points => new(_points)
```

The distribution points.

### `Parse`

__static__

```csharp
static CrlDistributionPointsExtension Parse(byte[] extnValue)
```

Parses a CRLDistributionPoints extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/CrlDistributionPointsExtension.cs`](../../../src/Chuvadi.Cryptography/X509/CrlDistributionPointsExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
