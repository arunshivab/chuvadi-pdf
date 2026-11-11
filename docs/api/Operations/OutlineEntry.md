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

### `OutlineEntry(string title, PdfDestination destination, IReadOnlyList<OutlineEntry> children)`

Initialises a bookmark that goes to an exact destination (3.33.0): the page, and how it is shown (`/XYZ` left, top and zoom, `/FitH` top and so on), written back as given. For a new or moved bookmark.

**Parameters**

- `title` — The bookmark's display title.
- `destination` — Where the bookmark goes; `PageIndex` is its page.
- `children` — The nested child bookmarks. <exception cref="ArgumentNullException">A parameter is null.</exception>

### `OutlineEntry(OutlineItem source, string title, IReadOnlyList<OutlineEntry> children)`

Initialises a bookmark kept from the document's outline (3.33.0), with a title and children that may be new. Its target (`/Dest`, including a named destination, or `/A`, any action), its colour and style, its structure element and whether it is open are written back exactly as they were. For an untouched or renamed bookmark.

**Parameters**

- `source` — The bookmark as `OutlineReader.GetOutlines` read it from the same document that is passed to `OutlineWriter.Apply`.
- `title` — The bookmark's display title.
- `children` — The nested child bookmarks. <exception cref="ArgumentNullException">A parameter is null.</exception>

## Properties

### `Title`

```csharp
string Title
```

Gets the bookmark's display title.

### `Destination`

```csharp
PdfDestination? Destination
```

Gets the exact destination the bookmark goes to, or null (3.33.0). Null for an entry made with a page index (it goes to the whole page, `/Fit`) or kept from a `Source`.

### `Source`

```csharp
OutlineItem? Source
```

Gets the bookmark this entry was kept from, or null (3.33.0). Its target, colour, style, structure element and open state are written back as they were.

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
