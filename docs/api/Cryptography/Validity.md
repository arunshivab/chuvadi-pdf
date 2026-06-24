# Validity

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The validity period of an X.509 certificate.

```csharp
public sealed class Validity
```

## Remarks

Structure: 
```
 Validity ::= SEQUENCE { notBefore  Time, notAfter   Time } Time ::= CHOICE { utcTime         UTCTime, generalizedTime GeneralizedTime } 
```
 Per RFC 5280 §4.1.2.5: certificates whose end date is before 2050 must use UTCTime; certificates whose end date is 2050 or later must use GeneralizedTime. Chuvadi tracks the original encoded form of each endpoint so it can re-serialise without changing the wire format.

## Properties

### `NotBefore`

```csharp
DateTimeOffset NotBefore
```

The start of the validity period (inclusive).

### `NotAfter`

```csharp
DateTimeOffset NotAfter
```

The end of the validity period (inclusive).

### `NotBeforeTag`

```csharp
Asn1UniversalTag NotBeforeTag
```

The original encoding (UTCTime or GeneralizedTime) of NotBefore.

### `NotAfterTag`

```csharp
Asn1UniversalTag NotAfterTag
```

The original encoding (UTCTime or GeneralizedTime) of NotAfter.

## Methods

### `IsWithin`

```csharp
bool IsWithin(DateTimeOffset instant)
```

True when `instant` lies within the validity period (inclusive at both endpoints).

### `ToString`

```csharp
override string ToString() => $"
```

<inheritdoc/>

### `Read`

__static__

```csharp
static Validity Read(Asn1Reader reader)
```

Reads a Validity from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/Validity.cs`](../../../src/Chuvadi.Cryptography/X509/Validity.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
