# Asn1Writer

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Build-style writer for nested ASN.1 DER structures.

```csharp
public sealed class Asn1Writer
```

## Remarks

Constructed types (SEQUENCE, SET, EXPLICIT tags) need their content length before the content can be emitted. This writer holds a stack of pending constructions, each backed by its own internal buffer. Calling `PushSequence` opens a new constructed scope; subsequent writes go into that scope until `PopSequence` closes it and flushes the assembled bytes into the parent scope (or the output stream if at the root).

## Constructors

### `Asn1Writer()`

Creates a writer accumulating to an internal buffer.

## Methods

### `ToArray`

```csharp
byte[] ToArray()
```

Returns the complete DER bytes. Must not have unclosed constructions.

### `PushSequence`

```csharp
void PushSequence() => Push(Asn1Tag.Constructed(Asn1UniversalTag.Sequence))
```

Opens a SEQUENCE scope. Subsequent writes accumulate as its content.

### `PopSequence`

```csharp
void PopSequence() => Pop(Asn1Tag.Constructed(Asn1UniversalTag.Sequence))
```

Closes the innermost SEQUENCE scope.

### `PushSet`

```csharp
void PushSet() => Push(Asn1Tag.Constructed(Asn1UniversalTag.Set))
```

Opens a SET scope.

### `PopSet`

```csharp
void PopSet() => Pop(Asn1Tag.Constructed(Asn1UniversalTag.Set))
```

Closes the innermost SET scope.

### `PushExplicit`

```csharp
void PushExplicit(int tagNumber)
```

Opens an EXPLICIT context-specific [n] scope.

### `PopExplicit`

```csharp
void PopExplicit(int tagNumber)
```

Closes the innermost EXPLICIT scope.

### `WriteBoolean`

```csharp
void WriteBoolean(bool value) => Asn1Boolean.Write(Current, value)
```

Writes a BOOLEAN.

### `WriteInteger`

```csharp
void WriteInteger(BigInteger value) => Asn1Integer.Write(Current, value)
```

Writes an INTEGER.

### `WriteInteger`

```csharp
void WriteInteger(int value) => Asn1Integer.Write(Current, value)
```

Writes an INTEGER.

### `WriteInteger`

```csharp
void WriteInteger(long value) => Asn1Integer.Write(Current, value)
```

Writes an INTEGER.

### `WriteNull`

```csharp
void WriteNull() => Asn1Null.Write(Current)
```

Writes a NULL.

### `WriteOctetString`

```csharp
void WriteOctetString(ReadOnlySpan<byte> value) => Asn1OctetString.Write(Current, value)
```

Writes an OCTET STRING.

### `WriteBitString`

```csharp
void WriteBitString(BitStringValue value) => Asn1BitString.Write(Current, value)
```

Writes a BIT STRING.

### `WriteBitString`

```csharp
void WriteBitString(ReadOnlySpan<byte> bytes) => Asn1BitString.Write(Current, bytes)
```

Writes a BIT STRING with zero unused bits.

### `WriteObjectIdentifier`

```csharp
void WriteObjectIdentifier(ObjectIdentifier oid) => Asn1ObjectIdentifier.Write(Current, oid)
```

Writes an OBJECT IDENTIFIER.

### `WriteUtf8String`

```csharp
void WriteUtf8String(string value) => Asn1String.WriteUtf8(Current, value)
```

Writes a UTF8String.

### `WritePrintableString`

```csharp
void WritePrintableString(string value) => Asn1String.WritePrintable(Current, value)
```

Writes a PrintableString.

### `WriteIA5String`

```csharp
void WriteIA5String(string value) => Asn1String.WriteIA5(Current, value)
```

Writes an IA5String.

### `WriteBmpString`

```csharp
void WriteBmpString(string value) => Asn1String.WriteBmp(Current, value)
```

Writes a BMPString.

### `WriteUtcTime`

```csharp
void WriteUtcTime(DateTimeOffset value) => Asn1Time.WriteUtcTime(Current, value)
```

Writes a UTCTime.

### `WriteGeneralizedTime`

```csharp
void WriteGeneralizedTime(DateTimeOffset value) => Asn1Time.WriteGeneralizedTime(Current, value)
```

Writes a GeneralizedTime.

### `WriteEncoded`

```csharp
void WriteEncoded(ReadOnlySpan<byte> encoded)
```

Writes a raw pre-encoded ASN.1 element. The caller is responsible for the bytes being valid DER; useful when copying TBS regions verbatim.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Writer.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Writer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
