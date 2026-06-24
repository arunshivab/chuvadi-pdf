# TstInfo

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

The structured timestamp content inside a TimeStampToken.

```csharp
public sealed class TstInfo
```

## Remarks

RFC 3161 §2.4.2: 
```
 TSTInfo ::= SEQUENCE  { version          INTEGER  { v1(1) }, policy           TSAPolicyId, messageImprint   MessageImprint, serialNumber     INTEGER, genTime          GeneralizedTime, accuracy         Accuracy                 OPTIONAL, ordering         BOOLEAN             DEFAULT FALSE, nonce            INTEGER                  OPTIONAL, tsa              [0] EXPLICIT GeneralName OPTIONAL, extensions       [1] IMPLICIT Extensions  OPTIONAL } 
```
 Chuvadi parses the mandatory fields and the most useful optional ones (genTime, messageImprint, serialNumber); other optional fields are preserved as raw bytes in `RawEncoding` for advanced callers.

## Properties

### `Version`

```csharp
int Version
```

Version of TSTInfo (per RFC 3161 the only currently-defined value is 1).

### `Policy`

```csharp
ObjectIdentifier Policy
```

The TSA's policy under which this token was issued.

### `MessageImprint`

```csharp
MessageImprint MessageImprint
```

The hash this token is asserting an existence-at-time claim for.

### `SerialNumber`

```csharp
BigInteger SerialNumber
```

The TSA's unique serial number for this token.

### `GenTime`

```csharp
DateTimeOffset GenTime
```

The time the TSA generated the token.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The full DER bytes of TSTInfo.

## Methods

### `Decode`

__static__

```csharp
static TstInfo Decode(byte[] der)
```

Parses a TstInfo from its DER encoding.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TstInfo.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TstInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
