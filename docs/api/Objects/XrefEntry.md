# XrefEntry

**Struct** in `Chuvadi.Pdf.Objects` (Objects)

Represents one entry in a PDF cross-reference table or stream.

```csharp
public readonly struct XrefEntry : IEquatable<XrefEntry>
```

## Remarks

In the classic xref table (PDF 32000-1:2008 §7.5.4), each entry is 20 bytes: a 10-digit byte offset, a 5-digit generation number, and a keyword ('n' for in-use, 'f' for free). In cross-reference streams (PDF 1.5+, §7.5.8), entries are encoded as binary integers with configurable field widths. This struct unifies both formats.

## Constructors

### `XrefEntry(int objectNumber, int generation, long byteOffset)`

Initialises a new in-use `XrefEntry` pointing to a byte offset in the PDF file.

## Properties

### `ObjectNumber`

```csharp
int ObjectNumber
```

Gets the object number this entry describes.

### `Generation`

```csharp
int Generation
```

Gets the generation number.

### `Type`

```csharp
XrefEntryType Type
```

Gets the type of this xref entry.

### `ByteOffset`

```csharp
long ByteOffset
```

For `XrefEntryType.InUse`: the byte offset of the object in the PDF file. For `XrefEntryType.Free`: the object number of the next free object.

### `StreamObjectNumber`

```csharp
int StreamObjectNumber
```

For `XrefEntryType.Compressed`: the object number of the containing object stream.

### `IndexInStream`

```csharp
int IndexInStream
```

For `XrefEntryType.Compressed`: the zero-based index of this object within the object stream.

### `IsInUse`

```csharp
bool IsInUse => Type == XrefEntryType.InUse
```

Returns true when this is an in-use entry.

### `IsFree`

```csharp
bool IsFree => Type == XrefEntryType.Free
```

Returns true when this is a free entry.

### `IsCompressed`

```csharp
bool IsCompressed => Type == XrefEntryType.Compressed
```

Returns true when this is a compressed entry.

## Methods

### `Free`

__static__

```csharp
static XrefEntry Free(int objectNumber, int generation, int nextFreeObjectNumber)
```

Initialises a new free `XrefEntry`.

**Parameters**

- `objectNumber` — This object's number.
- `generation` — Generation to use if the object is reused.
- `nextFreeObjectNumber` — Object number of the next free object (linked list of free objects).

### `Compressed`

__static__

```csharp
static XrefEntry Compressed(int objectNumber, int streamObjectNumber, int indexInStream)
```

Initialises a new compressed `XrefEntry` for an object stored inside an object stream (PDF 1.5+).

**Parameters**

- `objectNumber` — This object's number.
- `streamObjectNumber` — Object number of the object stream containing this object.
- `indexInStream` — Zero-based index of this object within the object stream.

### `PdfObjectId`

```csharp
PdfObjectId ObjectId => new PdfObjectId(ObjectNumber, Generation)
```

Gets the `PdfObjectId` for this entry.

### `==`

__static__

```csharp
static bool operator ==(XrefEntry left, XrefEntry right) => left.Equals(right)
```

Value equality.

### `!=`

__static__

```csharp
static bool operator !=(XrefEntry left, XrefEntry right) => !left.Equals(right)
```

Value inequality.

---

_Source: [`src/Chuvadi.Pdf.Objects/XrefEntry.cs`](../../../src/Chuvadi.Pdf.Objects/XrefEntry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
