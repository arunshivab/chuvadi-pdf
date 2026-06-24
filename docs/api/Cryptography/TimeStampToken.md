# TimeStampToken

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

An RFC 3161 TimeStampToken — a CMS SignedData wrapping a TSTInfo payload.

```csharp
public sealed class TimeStampToken
```

## Remarks

RFC 3161 §2.4.2: `TimeStampToken ::= ContentInfo` where the inner content type is `id-signedData` and the `encapContentInfo` of that SignedData carries `id-ct-TSTInfo` as its content type with the DER encoding of `TstInfo` as its content.

## Constructors

### `TimeStampToken(SignedData signedData, TstInfo tstInfo, byte[] rawEncoding)`

Initialises a new TimeStampToken.

## Properties

### `SignedData`

```csharp
SignedData SignedData
```

The underlying CMS SignedData (the signed bytes are `TstInfo`.RawEncoding).

### `TstInfo`

```csharp
TstInfo TstInfo
```

The decoded TSTInfo payload.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The full DER bytes of the TimeStampToken (i.e. of the outer ContentInfo).

## Methods

### `Decode`

__static__

```csharp
static TimeStampToken Decode(byte[] der)
```

Parses a TimeStampToken from its DER encoding. <exception cref="Asn1.Asn1Exception">If the bytes are not a CMS SignedData wrapping TSTInfo.</exception>

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampToken.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampToken.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
