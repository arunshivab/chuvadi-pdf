# OutlineEntry

**Class** in `Chuvadi.Pdf.Operations` (Operations)

A bookmark to write into a document outline: a title, the zero-based page it targets, and optional nested children. Used as input to `OutlineWriter`. The companion read-side type is `Chuvadi.Pdf.Forms.OutlineItem`. PDF 32000-1:2008 §12.3.3 — Document outline.

```csharp
public sealed class OutlineEntry
```

## Constructors

### `OutlineEntry(string title, int pageIndex)`

Initialises a bookmark with no children.

**Parameters**

- `title` — The bookmark's display title.
- `pageIndex` — The zero-based destination page index.

### `OutlineEntry(string title, int pageIndex, IReadOnlyList<OutlineEntry> children)`

Initialises a bookmark with nested children.

**Parameters**

- `title` — The bookmark's display title.
- `pageIndex` — The zero-based destination page index.
- `children` — The nested child bookmarks. <exception cref="ArgumentNullException"> Thrown when `title` or `children` is null. </exception>

## Properties

### `Title`

```csharp
string Title
```

Gets the bookmark's display title.

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based destination page index.

### `Children`

```csharp
IReadOnlyList<OutlineEntry> Children
```

Gets the nested child bookmarks, if any.

---

_Source: [`src/Chuvadi.Pdf.Operations/OutlineEntry.cs`](../../../src/Chuvadi.Pdf.Operations/OutlineEntry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
