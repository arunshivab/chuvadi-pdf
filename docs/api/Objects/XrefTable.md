# XrefTable

**Class** in `Chuvadi.Pdf.Objects` (Objects)

Represents a classic PDF cross-reference table.

```csharp
public sealed class XrefTable
```

## Remarks

The classic xref table is the original PDF cross-reference format, used in PDF 1.0 through 1.4 and still common in PDF 1.5+ for compatibility. Format in the PDF file: 
```
 xref 0 6 0000000000 65535 f 0000000015 00000 n 0000000108 00000 n ... 
```
 Each section starts with an object number and count. Each entry is exactly 20 bytes: 10-digit offset, space, 5-digit generation, space, one-character type ('n' or 'f'), carriage-return or space, line-feed. PDF 32000-1:2008 §7.5.4 — Cross-reference table.

## Constructors

### `XrefTable()`

Creates an empty `XrefTable`.

## Properties

### `Count`

```csharp
int Count => _entries.Count
```

Gets the number of entries in the table.

### `Entries`

```csharp
IEnumerable<XrefEntry> Entries => _entries.Values
```

Gets all entries in the table.

## Methods

### `Set`

```csharp
void Set(XrefEntry entry)
```

Adds or replaces an entry in the table.

### `Remove`

```csharp
bool Remove(int objectNumber)
```

Removes an entry from the table. The entry is NOT replaced with a free entry — use `Free` to mark an object as free.

### `Free`

```csharp
void Free(int objectNumber, int generation)
```

Marks the given object as free, adding it to the free list.

### `TryGet`

```csharp
bool TryGet(int objectNumber, out XrefEntry entry)
```

Attempts to look up an entry by object number.

### `Contains`

```csharp
bool Contains(int objectNumber)
```

Returns true when the object number has an in-use entry.

### `ContainsAny`

```csharp
bool ContainsAny(int objectNumber)
```

Returns true when the object number has an entry of any kind — in-use, compressed, or free.

**Remarks:** Incremental-update chains are read newest-section-first, and the entry in the most recent section supersedes all earlier ones regardless of kind (PDF 32000-1:2008 §7.5.6): a free entry in a newer section marks the object as deleted and must shadow any older definition. Merge logic therefore keys "already decided" off this method, not `Contains` (which reports only in-use entries and would let an older section resurrect a deleted object or replace a compressed entry).

### `GetOffset`

```csharp
long GetOffset(int objectNumber)
```

Gets the byte offset for an in-use object. Returns -1 when the object is not in the table or is free.

### `Write`

```csharp
long Write(Stream output)
```

Writes the xref table to `output` in PDF classic format. Returns the byte offset of the xref keyword in the stream.

**Remarks:** Entries are written in ascending object number order. Contiguous ranges are grouped into subsections as required by the spec. Each entry is exactly 20 bytes including the line ending. PDF 32000-1:2008 §7.5.4.

### `Parse`

__static__

```csharp
static XrefTable Parse(Stream input)
```

Parses a classic xref table from `input`. The stream must be positioned immediately after the 'xref' keyword.

**Returns:** A populated `XrefTable`. <exception cref="PdfParseException"> Thrown when the xref table is malformed. </exception>

---

_Source: [`src/Chuvadi.Pdf.Objects/XrefTable.cs`](../../../src/Chuvadi.Pdf.Objects/XrefTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
