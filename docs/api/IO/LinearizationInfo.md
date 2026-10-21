# LinearizationInfo

**Class** in `Chuvadi.Pdf.IO` (IO)

Parsed view of a PDF's linearization parameter dictionary.

```csharp
public sealed class LinearizationInfo
```

## Remarks

Per ISO 32000-1:2008 §F.2 a linearized PDF's first object (or one very close to the head) is a dictionary with /Linearized set to 1.0 and other entries describing the layout. This class exposes those entries in a strongly-typed form. Returned by `LinearizationReader.TryRead(Chuvadi.Pdf.Objects.PdfObjectStore)` when the document is linearized.

## Constructors

### `LinearizationInfo(double linearizedVersion, long fileLength, IReadOnlyList<long> hintOffsetsAndLengths, int firstPageObjectNumber, long endOfFirstPage, int pageCount, long mainXrefOffset)`

Initialises a new `LinearizationInfo`.

## Properties

### `LinearizedVersion`

```csharp
double LinearizedVersion
```

/Linearized — version number, always 1.0 in practice.

### `FileLength`

```csharp
long FileLength
```

/L — total file length in bytes.

### `HintOffsetsAndLengths`

```csharp
IReadOnlyList<long> HintOffsetsAndLengths
```

/H — flattened array of [offset, length] pairs locating each hint stream. Always 2 or 4 entries: primary hint stream alone (2) or primary + shared (4).

### `FirstPageObjectNumber`

```csharp
int FirstPageObjectNumber
```

/O — object number of the page dictionary for page 1.

### `EndOfFirstPage`

```csharp
long EndOfFirstPage
```

/E — byte offset of the end of page 1's first-page section.

### `PageCount`

```csharp
int PageCount
```

/N — number of pages in the document.

### `MainXrefOffset`

```csharp
long MainXrefOffset
```

/T — byte offset of the main (end-of-file) xref table.

---

_Source: [`src/Chuvadi.Pdf.IO/LinearizationInfo.cs`](../../../src/Chuvadi.Pdf.IO/LinearizationInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
