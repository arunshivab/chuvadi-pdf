# Asn1Integer

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 INTEGER values.

```csharp
public static class Asn1Integer
```

## Remarks

X.690 §8.3 encodes INTEGER as a two's-complement big-endian byte sequence. X.690 §8.3.2 requires the encoding to use the fewest possible octets: the first two bytes must not both be 0x00, and must not both be 0xFF. We enforce this on encode (always emit minimum length) and on decode (reject non-minimal encodings — strict DER). 

 Backed by `BigInteger` so the full range of public-key moduli, signature values, and certificate serial numbers (commonly 128 bits or more) round-trips losslessly. Convenience overloads for `int` and `long` are provided for tag numbers and small constants.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, BigInteger value)
```

Writes a BigInteger as ASN.1 INTEGER (DER, minimal octets).

### `Write`

__static__

```csharp
static void Write(Stream output, int value) => Write(output, new BigInteger(value))
```

Writes a 32-bit integer as ASN.1 INTEGER (DER).

### `Write`

__static__

```csharp
static void Write(Stream output, long value) => Write(output, new BigInteger(value))
```

Writes a 64-bit integer as ASN.1 INTEGER (DER).

### `EncodeContent`

__static__

```csharp
static byte[] EncodeContent(BigInteger value)
```

Returns the DER-encoded content octets for the given value (without tag/length).

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out BigInteger value)
```

Reads an INTEGER. Returns the offset just past it.

### `DecodeContent`

__static__

```csharp
static BigInteger DecodeContent(byte[] source, int contentOffset, int length, long errorOffset)
```

Decodes INTEGER content octets without the tag/length wrapper. Enforces the DER minimum-octets rule. <exception cref="Asn1Exception">If the encoding is non-minimal or empty.</exception>

### `ReadInt32`

__static__

```csharp
static int ReadInt32(byte[] source, int offset, out int after)
```

Reads an INTEGER and converts to int, rejecting overflow.

### `ReadInt64`

__static__

```csharp
static long ReadInt64(byte[] source, int offset, out int after)
```

Reads an INTEGER and converts to long, rejecting overflow.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Integer.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Integer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
