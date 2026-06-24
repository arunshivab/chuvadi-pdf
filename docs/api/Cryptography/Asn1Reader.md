# Asn1Reader

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Pull-style reader for nested ASN.1 BER/DER structures.

```csharp
public sealed class Asn1Reader
```

## Remarks

Construct one over a byte buffer, then call methods that match the expected shape: `ReadSequence` opens a SEQUENCE and returns a sub-reader bounded by its content; `ReadInteger` reads an INTEGER value and advances. Each sub-reader is a separate view over the same buffer with its own bounds — you can have multiple sub-readers active at once, but each must be closed (via `ExpectEnd`) before its parent is.

## Constructors

### `Asn1Reader(byte[] source)`

Constructs a reader over the entire buffer.

## Properties

### `IsAtEnd`

```csharp
bool IsAtEnd => _pos >= _end
```

True when no more bytes remain in this view.

### `Position`

```csharp
int Position => _pos
```

The current byte offset within the underlying source.

## Methods

### `ExpectEnd`

```csharp
void ExpectEnd()
```

Throws if any bytes remain unread.

### `PeekTag`

```csharp
Asn1Tag PeekTag()
```

Peeks at the next tag without consuming any bytes.

### `TryPeekTag`

```csharp
bool TryPeekTag(Asn1Tag expected)
```

Returns true when the next element's tag matches the given expected tag.

### `ReadSequence`

```csharp
Asn1Reader ReadSequence() => ReadConstructed(Asn1UniversalTag.Sequence)
```

Opens a SEQUENCE. Returns a sub-reader bounded by its content.

### `ReadSet`

```csharp
Asn1Reader ReadSet() => ReadConstructed(Asn1UniversalTag.Set)
```

Opens a SET. Returns a sub-reader bounded by its content.

### `ReadBoolean`

```csharp
bool ReadBoolean()
```

Reads a BOOLEAN.

### `ReadInteger`

```csharp
BigInteger ReadInteger()
```

Reads an INTEGER as BigInteger.

### `ReadInt32`

```csharp
int ReadInt32()
```

Reads an INTEGER constrained to Int32.

### `ReadNull`

```csharp
void ReadNull()
```

Reads a NULL.

### `ReadOctetString`

```csharp
byte[] ReadOctetString()
```

Reads an OCTET STRING.

### `ReadBitString`

```csharp
BitStringValue ReadBitString()
```

Reads a BIT STRING.

### `ReadObjectIdentifier`

```csharp
ObjectIdentifier ReadObjectIdentifier()
```

Reads an OBJECT IDENTIFIER.

### `ReadUtf8String`

```csharp
string ReadUtf8String()
```

Reads a UTF8String.

### `ReadPrintableString`

```csharp
string ReadPrintableString()
```

Reads a PrintableString.

### `ReadIA5String`

```csharp
string ReadIA5String()
```

Reads an IA5String.

### `ReadBmpString`

```csharp
string ReadBmpString()
```

Reads a BMPString.

### `ReadUtcTime`

```csharp
DateTimeOffset ReadUtcTime()
```

Reads a UTCTime.

### `ReadGeneralizedTime`

```csharp
DateTimeOffset ReadGeneralizedTime()
```

Reads a GeneralizedTime.

### `ReadExplicit`

```csharp
Asn1Reader ReadExplicit(int tagNumber)
```

Reads an EXPLICITLY tagged context-specific element by descending into it and returning the inner sub-reader.

### `ReadImplicitOctets`

```csharp
byte[] ReadImplicitOctets(int tagNumber)
```

Reads an IMPLICITLY tagged element. The caller specifies what underlying universal type it represents; the tag class/number are checked but the inner content is parsed as if the universal tag were present.

### `HasContextSpecific`

```csharp
bool HasContextSpecific(int tagNumber)
```

Returns true if a context-specific [`tagNumber`] element is next. Useful for OPTIONAL fields.

### `PeekEncoded`

```csharp
byte[] PeekEncoded()
```

Returns the complete encoded bytes of the next element (tag, length, content) without consuming it. Useful for capturing a region while still needing to parse it.

### `ReadEncoded`

```csharp
byte[] ReadEncoded()
```

Reads the next element and returns its complete encoded bytes including tag and length. Useful for capturing TBS regions for signature verification.

### `Skip`

```csharp
void Skip()
```

Skips the next element regardless of tag.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Reader.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Reader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
