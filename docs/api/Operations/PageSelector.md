# PageSelector

**Struct** in `Chuvadi.Pdf.Operations` (Operations)

Identifies a single source page for `PageOperations.Assemble(System.IO.Stream, System.Collections.Generic.IReadOnlyList{PageSelector})`: a source document paired with a zero-based page index. The same selector — or the same document with different indices — may appear any number of times in an assembly list, which is how duplicate and interleaved output pages are expressed.

```csharp
public readonly struct PageSelector : IEquatable<PageSelector>
```

## Constructors

### `PageSelector(PdfDocument document, int pageIndex)`

Initialises a selector for one page of a source document.

**Parameters**

- `document` — The source document the page is drawn from.
- `pageIndex` — The zero-based index of the page within `document`. <exception cref="ArgumentNullException">Thrown when `document` is null.</exception>

## Properties

### `Document`

```csharp
PdfDocument Document
```

Gets the source document the page is drawn from.

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based index of the page within `Document`.

## Methods

### `Equals`

```csharp
bool Equals(PageSelector other)
```

Determines whether this selector equals `other`: the same document instance (by reference) and the same page index.

**Parameters**

- `other` — The selector to compare with.

**Returns:** True when both refer to the same document and page index.

### `Equals`

```csharp
override bool Equals(object? obj)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode()
```

<inheritdoc/>

### `==`

__static__

```csharp
static bool operator ==(PageSelector left, PageSelector right)
```

Determines whether two selectors are equal.

**Parameters**

- `left` — The first selector.
- `right` — The second selector.

**Returns:** True when the selectors are equal.

### `!=`

__static__

```csharp
static bool operator !=(PageSelector left, PageSelector right)
```

Determines whether two selectors are unequal.

**Parameters**

- `left` — The first selector.
- `right` — The second selector.

**Returns:** True when the selectors are unequal.

---

_Source: [`src/Chuvadi.Pdf.Operations/PageSelector.cs`](../../../src/Chuvadi.Pdf.Operations/PageSelector.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
