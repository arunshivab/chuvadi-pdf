# PdfToken

**Struct** in `Chuvadi.Pdf.Primitives` (Primitives)

A lightweight token produced by `PdfTokenizer`.

```csharp
public readonly struct PdfToken : IEquatable<PdfToken>
```

## Remarks

A token is a (type, raw-bytes, byte-offset) triple. The raw bytes are the exact bytes from the PDF stream that form the token, including delimiters such as parentheses for literal strings and angle brackets for hex strings — but NOT including the leading solidus for names. PdfToken is a readonly struct to keep the tokenizer allocation-free. Callers that need to retain a token beyond the next Read() call must copy the bytes from `RawBytes`. PDF 32000-1:2008 §7.2 — Lexical conventions.

## Constructors

### `PdfToken(PdfTokenType type, byte[] rawBytes, long byteOffset)`

Initialises a new `PdfToken`.

**Parameters**

- `type` — The token type.
- `rawBytes` — The raw bytes of the token as they appear in the PDF stream. The array is owned by the tokenizer — copy if you need to keep it.
- `byteOffset` — The byte offset in the stream at which this token begins.

## Properties

### `Type`

```csharp
PdfTokenType Type
```

Gets the type of this token.

### `RawBytes`

```csharp
byte[] RawBytes
```

Gets the raw bytes of this token as they appear in the PDF stream.

### `ByteOffset`

```csharp
long ByteOffset
```

Gets the byte offset in the underlying stream at which this token begins. Used for error reporting and xref validation.

### `IsEndOfStream`

```csharp
bool IsEndOfStream => Type == PdfTokenType.EndOfStream
```

Returns true if this token has type `PdfTokenType.EndOfStream`.

## Methods

### `new`

__static__

```csharp
static readonly PdfToken EndOfStream = new(PdfTokenType.EndOfStream, [], -1)
```

A sentinel token representing the end of the stream.

### `Encoding.Latin1.GetString`

```csharp
string RawText => Encoding.Latin1.GetString(RawBytes)
```

Returns the raw bytes decoded as a Latin-1 string. Useful for keyword tokens and for debugging.

### `GetHashCode`

```csharp
override int GetHashCode() => HashCode.Combine(Type, ByteOffset)
```

<inheritdoc/>

### `==`

__static__

```csharp
static bool operator ==(PdfToken left, PdfToken right) => left.Equals(right)
```

Value equality.

### `!=`

__static__

```csharp
static bool operator !=(PdfToken left, PdfToken right) => !left.Equals(right)
```

Value inequality.

### `ToString`

```csharp
override string ToString()
```

Returns a human-readable description of this token, suitable for error messages and debug output.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfToken.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfToken.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
