# Asn1OctetString

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 OCTET STRING values.

```csharp
public static class Asn1OctetString
```

## Remarks

X.690 §8.7 permits both primitive and constructed encodings of OCTET STRING. Strict DER (§10.2) requires primitive form. Chuvadi always emits primitive and accepts primitive only on the read side. Constructed OCTET STRING with indefinite length (which BER allows) is the source of several historical signature-validation CVEs; rejecting it eliminates that attack surface.

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, ReadOnlySpan<byte> value)
```

Writes `value` as a primitive OCTET STRING.

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out byte[] value)
```

Reads an OCTET STRING and returns its content bytes (a fresh copy).

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1OctetString.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1OctetString.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
