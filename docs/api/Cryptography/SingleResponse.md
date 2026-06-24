# SingleResponse

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

One certificate's entry within an OCSP response's `responses` field.

```csharp
public sealed class SingleResponse
```

## Remarks

RFC 6960 §4.2.1: 
```
 SingleResponse ::= SEQUENCE { certID            CertID, certStatus        CertStatus, thisUpdate        GeneralizedTime, nextUpdate        [0] EXPLICIT GeneralizedTime OPTIONAL, singleExtensions  [1] EXPLICIT Extensions OPTIONAL } 
```

## Properties

### `CertId`

```csharp
CertId CertId
```

The certificate this entry is about.

### `Status`

```csharp
CertStatus Status
```

The responder's verdict.

### `ThisUpdate`

```csharp
DateTimeOffset ThisUpdate
```

The time the status information was generated.

### `NextUpdate`

```csharp
DateTimeOffset? NextUpdate
```

The latest time newer information will be available. May be absent.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/SingleResponse.cs`](../../../src/Chuvadi.Cryptography/Ocsp/SingleResponse.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
