# BitStringValue

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

A decoded ASN.1 BIT STRING — an octet sequence plus a count of unused trailing bits in the final octet.

```csharp
public sealed class BitStringValue
```

## Constructors

### `BitStringValue(byte[] bytes, int unusedBitsInFinalOctet)`

Initialises a new BitStringValue.

**Parameters**

- `bytes` — The bit string content as packed bytes, big-endian.
- `unusedBitsInFinalOctet` — 0..7 — bits to ignore at the end.

## Properties

### `Bytes`

```csharp
byte[] Bytes
```

The packed bytes (big-endian bit ordering).

### `UnusedBitsInFinalOctet`

```csharp
int UnusedBitsInFinalOctet
```

Number of bits in the final octet that are not part of the value.

## Methods

### `=>`

```csharp
int BitLength => (Bytes.Length * 8) - UnusedBitsInFinalOctet
```

Total number of bits represented.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1BitString.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1BitString.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
