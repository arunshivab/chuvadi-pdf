# PdfName

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF name object (e.g. `/Type`, `/Page`). Names compare by value: two names are equal when their decoded values are equal (ordinal), whether or not they are the same instance. PDF 32000-1:2008 §7.3.5 — Name objects.

```csharp
public sealed class PdfName : PdfPrimitive, IEquatable<PdfName>
```

## Remarks

Since 3.31.2 (D-219) names are no longer kept for the life of the process. The names Chuvadi itself uses, and the first 4,096 other names of up to 64 characters, share one instance each, so common lookups never allocate. Any other name is a new instance, freed with the document that read it. Before 3.31.2 every name read from any file was interned forever and compared by reference, so a server's memory grew with every new name it saw.  

 Use `==`, `Equals(PdfName?)` or a dictionary to compare names. `object.ReferenceEquals(object?, object?)` can be false for two equal names that are not in the shared table.

## Constructors

### `static implicit operator PdfName(string value) => Intern(value)`

Implicit conversion from string — interns the name.

## Properties

### `Value`

```csharp
string Value
```

Gets the decoded name value, without the leading solidus. For example, for the PDF token `/FlateDecode`, this is `"FlateDecode"`.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Name
```

<inheritdoc/>

## Methods

### `Intern`

__static__

```csharp
static readonly PdfName Type = Intern("Type")
```

### `Intern`

__static__

```csharp
static readonly PdfName Subtype = Intern("Subtype")
```

### `Intern`

__static__

```csharp
static readonly PdfName Page = Intern("Page")
```

### `Intern`

__static__

```csharp
static readonly PdfName Pages = Intern("Pages")
```

### `Intern`

__static__

```csharp
static readonly PdfName Catalog = Intern("Catalog")
```

### `Intern`

__static__

```csharp
static readonly PdfName Kids = Intern("Kids")
```

### `Intern`

__static__

```csharp
static readonly PdfName Parent = Intern("Parent")
```

### `Intern`

__static__

```csharp
static readonly PdfName Count = Intern("Count")
```

### `Intern`

__static__

```csharp
static readonly PdfName Contents = Intern("Contents")
```

### `Intern`

__static__

```csharp
static readonly PdfName Resources = Intern("Resources")
```

### `Intern`

__static__

```csharp
static readonly PdfName MediaBox = Intern("MediaBox")
```

### `Intern`

__static__

```csharp
static readonly PdfName CropBox = Intern("CropBox")
```

### `Intern`

__static__

```csharp
static readonly PdfName Rotate = Intern("Rotate")
```

### `Intern`

__static__

```csharp
static readonly PdfName Filter = Intern("Filter")
```

### `Intern`

__static__

```csharp
static readonly PdfName Length = Intern("Length")
```

### `Intern`

__static__

```csharp
static readonly PdfName FlateDecode = Intern("FlateDecode")
```

### `Intern`

__static__

```csharp
static readonly PdfName Font = Intern("Font")
```

### `Intern`

__static__

```csharp
static readonly PdfName XObject = Intern("XObject")
```

### `Intern`

__static__

```csharp
static readonly PdfName Outlines = Intern("Outlines")
```

### `Intern`

__static__

```csharp
static readonly PdfName Info = Intern("Info")
```

### `Intern`

__static__

```csharp
static readonly PdfName Root = Intern("Root")
```

### `Intern`

__static__

```csharp
static readonly PdfName Size = Intern("Size")
```

### `Intern`

__static__

```csharp
static readonly PdfName Prev = Intern("Prev")
```

### `Intern`

__static__

```csharp
static PdfName Intern(string value)
```

Returns the `PdfName` for the given decoded value: the shared instance when the name is well known or cached, otherwise a new instance that compares equal to every other instance with this value. <exception cref="ArgumentException"> Thrown when `value` is null or empty. </exception>

### `FromRawBytes`

__static__

```csharp
static PdfName FromRawBytes(ReadOnlySpan<byte> rawBytes)
```

Parses and interns a `PdfName` from raw PDF bytes, decoding `#XX` escape sequences.

**Parameters**

- `rawBytes` — The raw name bytes from the content stream or object parser, excluding the leading solidus.

**Returns:** The interned `PdfName` for the decoded value. <exception cref="PdfParseException"> Thrown when `rawBytes` decodes to an empty name. An empty name is not valid PDF syntax; because this method is the parser entry point for untrusted document bytes, the failure is surfaced as a catchable parse error rather than the argument-validation exception thrown by `Intern(string)`. </exception>

### `Equals`

```csharp
override bool Equals(object? obj) => obj is PdfName other && Equals(other)
```

Returns whether `obj` is a name with the same decoded value.

### `GetHashCode`

```csharp
override int GetHashCode() => _hash
```

Returns the ordinal hash of the decoded value.

### `==`

__static__

```csharp
static bool operator ==(PdfName? left, PdfName? right) => left is null ? right is null : left.Equals(right)
```

Returns whether two names have the same decoded value.

### `!=`

__static__

```csharp
static bool operator !=(PdfName? left, PdfName? right) => !(left == right)
```

Returns whether two names have different decoded values.

### `ToString`

```csharp
override string ToString()
```

Returns the PDF syntax representation including the leading solidus, e.g. `/FlateDecode`. Characters requiring encoding are written as `#XX`.

### `string`

__static__

```csharp
static implicit operator string(PdfName name)
```

Implicit conversion to string — returns `Value`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfName.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfName.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
