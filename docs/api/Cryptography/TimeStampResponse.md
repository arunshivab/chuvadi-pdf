# TimeStampResponse

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

An RFC 3161 Time-Stamp Protocol response, as returned by a TSA.

```csharp
public sealed class TimeStampResponse
```

## Remarks

ASN.1 structure (RFC 3161 §2.4.2): 
```
 TimeStampResp ::= SEQUENCE { status         PKIStatusInfo, timeStampToken TimeStampToken  OPTIONAL } PKIStatusInfo ::= SEQUENCE { status         PKIStatus, statusString   PKIFreeText  OPTIONAL, failInfo       PKIFailureInfo  OPTIONAL } 
```
 On success (`TimeStampStatus.Granted` or `TimeStampStatus.GrantedWithMods`), `TimeStampToken` is non-null and carries the TSA's signed token.

## Properties

### `Status`

```csharp
TimeStampStatus Status
```

Decoded PKIStatus value.

### `StatusStrings`

```csharp
IReadOnlyList<string> StatusStrings
```

Human-readable status strings from the TSA, if any.

### `TimeStampToken`

```csharp
TimeStampToken? TimeStampToken
```

The timestamp token; non-null on success.

## Methods

### `Decode`

__static__

```csharp
static TimeStampResponse Decode(byte[] der)
```

Decodes a DER-encoded RFC 3161 TimeStampResp. Throws on malformed input.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampResponse.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampResponse.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
