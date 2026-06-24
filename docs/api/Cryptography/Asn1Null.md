# Asn1Null

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 NULL values.

```csharp
public static class Asn1Null
```

## Remarks

A NULL has no content (X.690 §8.8.2). Its encoded form is always exactly the two bytes `05 00`: tag 5 universal primitive, length 0.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output)
```

Writes a NULL value at the current position of `output`.

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset)
```

Reads and validates a NULL value from `source` at `offset`. Returns the offset just past the encoded NULL.

## Fields

### `EncodedBytes`

__static__

```csharp
static readonly byte[] EncodedBytes = [0x05, 0x00]
```

The full DER encoding of NULL, ready to emit verbatim.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Null.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Null.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
