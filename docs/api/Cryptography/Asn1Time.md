# Asn1Time

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 UTCTime and GeneralizedTime values.

```csharp
public static class Asn1Time
```

## Remarks

Per RFC 5280 §4.1.2.5 (PKIX restrictions for X.509 certificate validity dates): 
 
- UTCTime is "YYMMDDhhmmssZ" (always with seconds, always Z suffix). 
- GeneralizedTime is "YYYYMMDDhhmmssZ" with no fractional seconds. 
- UTCTime year 00..49 maps to 2000..2049; year 50..99 maps to 1950..1999.  Chuvadi emits these strict forms only. On reading, accepts the RFC 5280 form plus the broader ASN.1 form (optional seconds in UTCTime, fractional seconds in GeneralizedTime) which appears in CAdES signatures.

## Methods

### `WriteUtcTime`

__static__

```csharp
static void WriteUtcTime(Stream output, DateTimeOffset value)
```

Writes a UTCTime in RFC 5280 form (YYMMDDhhmmssZ).

### `ReadUtcTime`

__static__

```csharp
static int ReadUtcTime(byte[] source, int offset, out DateTimeOffset value)
```

Reads a UTCTime.

### `WriteGeneralizedTime`

__static__

```csharp
static void WriteGeneralizedTime(Stream output, DateTimeOffset value)
```

Writes a GeneralizedTime in RFC 5280 form (YYYYMMDDhhmmssZ, no fractional seconds).

### `ReadGeneralizedTime`

__static__

```csharp
static int ReadGeneralizedTime(byte[] source, int offset, out DateTimeOffset value)
```

Reads a GeneralizedTime.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Time.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Time.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
