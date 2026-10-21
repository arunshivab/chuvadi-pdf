# Asn1TagLength

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Stateless low-level codec for ASN.1 BER/DER tag and length prefixes.

```csharp
public static class Asn1TagLength
```

## Remarks

Parsing an ASN.1 element decomposes naturally into three steps: read the tag, read the length, then read length bytes of value. This class handles only the first two steps. The contents bytes are returned as a span or byte range for the caller to interpret. 

 Both DER and BER encodings are accepted on the read side. Indefinite-length form (length octet 0x80 followed by content terminated by two zero bytes) is rejected by default — Chuvadi's signing workflows require DER, which forbids it. Writing always produces strict DER.

## Methods

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out Asn1Tag tag, out int contentOffset, out int contentLength)
```

Reads a tag and length from `source` starting at `offset`.

**Parameters**

- `source` — Byte array containing the ASN.1 element.
- `offset` — Position at which to begin reading.
- `tag` — On success, the decoded tag.
- `contentOffset` — On success, the offset of the first content byte.
- `contentLength` — On success, the length of the content in bytes.

**Returns:** The offset immediately after the encoded element (contentOffset + contentLength). <exception cref="ArgumentNullException">If source is null.</exception> <exception cref="Asn1Exception">If the tag or length is malformed, indefinite-length is used, or the element extends past the end of source.</exception>

### `Write`

__static__

```csharp
static void Write(Stream output, Asn1Tag tag, int contentLength)
```

Writes a tag and length prefix to `output` in strict DER form.

**Parameters**

- `output` — Writable destination stream.
- `tag` — The tag to write.
- `contentLength` — Length of the content that will follow. <exception cref="ArgumentNullException">If output is null.</exception> <exception cref="ArgumentOutOfRangeException">If contentLength is negative.</exception>

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1TagLength.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1TagLength.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
