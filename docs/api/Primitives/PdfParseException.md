# PdfParseException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when the bytes of a PDF cannot be parsed because they violate the PDF syntax: malformed tokens, structural errors in dictionaries or arrays, invalid integer or real literals, missing required keywords.

```csharp
public class PdfParseException : PdfException
```

## Remarks

Distinguished from `PdfCorruptionException` — that signals a document that <em>parses</em> but is semantically inconsistent (e.g. a cyclic page tree, a missing required catalog entry). A parse error means the bytes themselves are wrong; a corruption error means the bytes are fine but the document they describe is broken. In v2.0.0 this type replaces the v1.x `PdfReaderException`, `PdfTokenizerException`, and the structural-shape subset of `PdfObjectException`.

## Constructors

### `PdfParseException()`

Initialises a new instance with no message.

### `PdfParseException(string message) : base(message)`

Initialises a new instance with the given message.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfParseException(string message, Exception innerException) : base(message, innerException)`

Initialises a new instance with the given message and an inner exception that caused the failure (e.g. an `OverflowException` from a malformed integer literal).

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

### `PdfParseException(string message, long offset) : base(message)`

Initialises a new instance with the given message and a byte offset into the PDF input where the failure was detected.

**Parameters**

- `message` — A human-readable description of the failure.
- `offset` — Zero-based byte offset into the input stream.

### `PdfParseException(string message, long offset, Exception innerException) : base(message, innerException)`

Initialises a new instance with the given message, byte offset, and an inner exception that caused the failure.

**Parameters**

- `message` — A human-readable description of the failure.
- `offset` — Zero-based byte offset into the input stream.
- `innerException` — The exception that triggered this one.

## Properties

### `Offset`

```csharp
long? Offset
```

Zero-based byte offset into the source stream where the failure was detected, or `null` if the offset is not known.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfParseException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfParseException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
