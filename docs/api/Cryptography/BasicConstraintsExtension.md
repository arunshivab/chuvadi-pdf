# BasicConstraintsExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Basic Constraints extension — identifies CA certificates and bounds the depth of the chain they may issue.

```csharp
public sealed class BasicConstraintsExtension
```

## Remarks

Structure: 
```
 BasicConstraints ::= SEQUENCE { cA                BOOLEAN DEFAULT FALSE, pathLenConstraint INTEGER (0..MAX) OPTIONAL } 
```
 Per RFC 5280 §4.2.1.9, pathLenConstraint is meaningful only when cA is TRUE and the keyCertSign bit is set in KeyUsage. A value of N means at most N intermediate CA certificates may follow this one in a certification path.

## Constructors

### `BasicConstraintsExtension(bool isCa, int? pathLenConstraint)`

Initialises a new BasicConstraintsExtension.

## Properties

### `IsCa`

```csharp
bool IsCa
```

True when the subject is a Certification Authority.

### `PathLenConstraint`

```csharp
int? PathLenConstraint
```

The maximum path length constraint (null = unconstrained).

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.BasicConstraints
```

The OID identifying this extension.

## Methods

### `Parse`

__static__

```csharp
static BasicConstraintsExtension Parse(byte[] extnValue)
```

Parses a BasicConstraints extension from the raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/BasicConstraintsExtension.cs`](../../../src/Chuvadi.Cryptography/X509/BasicConstraintsExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
