# PdfString

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF string object. A PDF string is a sequence of bytes, not necessarily valid Unicode. Serialised as literal `(Hello)` or hex `&lt;48656C6C6F&gt;` form. PDF 32000-1:2008 §7.3.4 — String objects.

```csharp
public sealed class PdfString : PdfPrimitive, IEquatable<PdfString>
```

## Constructors

### `PdfString(ReadOnlySpan<byte> bytes, bool preferHexForm = false)`

Initialises a new `PdfString` with the given raw bytes.

**Parameters**

- `bytes` — The raw byte content. A copy is taken.
- `preferHexForm` — True to serialise in hex form; false for literal form.

### `PdfString(string value, bool preferHexForm = false)`

Initialises a new `PdfString` from a .NET string, encoded as Latin-1 (PDFDocEncoding for ASCII range).

### `static implicit operator PdfString(string value) => new(value)`

Implicit conversion from a .NET string using Latin-1 encoding.

## Properties

### `Bytes`

```csharp
byte[] Bytes
```

Gets the raw byte content of this string.

### `PreferHexForm`

```csharp
bool PreferHexForm
```

True if this string prefers hex serialisation form.

### `Length`

```csharp
int Length => Bytes.Length
```

Gets the length of the string in bytes.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.String
```

<inheritdoc/>

## Methods

### `new`

__static__

```csharp
static readonly PdfString Empty = new([], false)
```

The empty PDF string.

### `ToTextString`

```csharp
string ToTextString()
```

Decodes this PDF string as a text string. Uses UTF-16BE if the bytes begin with BOM 0xFE 0xFF, UTF-16LE if they begin with 0xFF 0xFE, UTF-8 if they begin with 0xEF 0xBB 0xBF, or PDFDocEncoding (Latin-1) otherwise.

### `Equals`

```csharp
bool Equals(PdfString? other)
```

Two strings are equal when their byte contents are identical. Serialisation form is not considered.

### `Equals`

```csharp
override bool Equals(object? obj) => Equals(obj as PdfString)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode()
```

<inheritdoc/>

### `ToString`

```csharp
override string ToString() => PreferHexForm ? ToHexForm() : ToLiteralForm()
```

<inheritdoc/>

### `FromUnicode`

__static__

```csharp
static PdfString FromUnicode(string value)
```

Creates a `PdfString` from a .NET string encoded as UTF-16BE with BOM for correct round-trip of non-Latin characters.

### `DecodeLiteralToken`

__static__

```csharp
static byte[] DecodeLiteralToken(ReadOnlySpan<byte> raw)
```

Decodes a literal string token's raw bytes into string content bytes, resolving every escape defined by PDF 32000-1:2008 §7.3.4.2.

**Parameters**

- `raw` — The token bytes, optionally including the parentheses.

**Returns:** The decoded string content bytes.

**Remarks:** Accepts the raw token with or without the wrapping parentheses. Handles: `\n \r \t \b \f \( \) \\`; one-to-three-digit octal escapes (high-order overflow ignored per spec); a reverse solidus at end-of-line as a line continuation (the solidus and the end-of-line marker are both dropped, CR LF counting as one marker); a reverse solidus before any other character is dropped, keeping the character; and an unescaped end-of-line marker inside the string is normalized to a single LF (0x0A) byte. This is the single authoritative decoder — the object parser, the content-stream parser, and the content-stream walker all delegate here so a string decodes identically on every path.

### `DecodeHexToken`

__static__

```csharp
static byte[] DecodeHexToken(ReadOnlySpan<byte> raw)
```

Decodes a hexadecimal string token's raw bytes into string content bytes per PDF 32000-1:2008 §7.3.4.3. Accepts the raw token with or without the wrapping angle brackets; whitespace between digits is ignored and a trailing odd digit is padded with zero.

**Parameters**

- `raw` — The token bytes, optionally including the brackets.

**Returns:** The decoded string content bytes.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfString.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfString.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
