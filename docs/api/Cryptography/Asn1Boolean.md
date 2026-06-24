# Asn1Boolean

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 BOOLEAN values.

```csharp
public static class Asn1Boolean
```

## Remarks

X.690 §8.2.2 requires the contents octet to be a single byte. BER allows any non-zero value to represent TRUE; DER (§11.1) restricts TRUE to exactly 0xFF. Chuvadi emits DER (always 0xFF for TRUE) and accepts both BER and DER on the read side, treating any non-zero content byte as TRUE.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, bool value)
```

Writes a BOOLEAN value in DER form.

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out bool value)
```

Reads a BOOLEAN value. Returns the offset just past the encoded value.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Boolean.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Boolean.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
