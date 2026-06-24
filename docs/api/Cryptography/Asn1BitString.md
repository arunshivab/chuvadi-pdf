# Asn1BitString

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 BIT STRING values.

```csharp
public static class Asn1BitString
```

## Remarks

X.690 §8.6.2 encodes BIT STRING with an "unused bits" leading byte indicating how many trailing bits of the final octet are padding. DER (§11.2.1) requires padding bits to be zero, and forbids constructed encoding.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, BitStringValue value)
```

Writes a BIT STRING value (primitive DER form).

### `Write`

__static__

```csharp
static void Write(Stream output, ReadOnlySpan<byte> bytes)
```

Writes a BIT STRING from raw bytes with zero unused bits.

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out BitStringValue value)
```

Reads a BIT STRING. Enforces DER (primitive only, padding bits zero).

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1BitString.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1BitString.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
