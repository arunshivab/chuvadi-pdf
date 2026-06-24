# Asn1String

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 character string types.

```csharp
public static class Asn1String
```

## Remarks

All these types share encoding shape (tag, length, content octets); they differ in character set: 
 
- UTF8String — UTF-8 encoded Unicode. 
- PrintableString — ASCII subset: A-Z a-z 0-9 space ' ( ) + , - . / : = ? 
- IA5String — full 7-bit ASCII (0-127). 
- T61String / TeletexString — legacy; Chuvadi reads as Latin-1 best-effort. 
- BMPString — UTF-16 Big-Endian Basic Multilingual Plane.

## Methods

### `WriteUtf8`

__static__

```csharp
static void WriteUtf8(Stream output, string value)
```

Writes a UTF8String value.

### `ReadUtf8`

__static__

```csharp
static int ReadUtf8(byte[] source, int offset, out string value)
```

Reads a UTF8String. Returns the offset just past the encoded value.

### `WritePrintable`

__static__

```csharp
static void WritePrintable(Stream output, string value)
```

Writes a PrintableString value. Throws if any character is outside the allowed subset.

### `ReadPrintable`

__static__

```csharp
static int ReadPrintable(byte[] source, int offset, out string value)
```

Reads a PrintableString.

### `WriteIA5`

__static__

```csharp
static void WriteIA5(Stream output, string value)
```

Writes an IA5String value (7-bit ASCII).

### `ReadIA5`

__static__

```csharp
static int ReadIA5(byte[] source, int offset, out string value)
```

Reads an IA5String.

### `WriteBmp`

__static__

```csharp
static void WriteBmp(Stream output, string value)
```

Writes a BMPString value (BMP only — no characters above U+FFFF).

### `ReadBmp`

__static__

```csharp
static int ReadBmp(byte[] source, int offset, out string value)
```

Reads a BMPString.

### `ReadT61`

__static__

```csharp
static int ReadT61(byte[] source, int offset, out string value)
```

Reads a T61String / TeletexString as Latin-1 best-effort.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1String.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1String.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
