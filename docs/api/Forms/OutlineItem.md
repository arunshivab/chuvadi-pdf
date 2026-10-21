# OutlineItem

**Class** in `Chuvadi.Pdf.Forms` (Forms)

A single bookmark in the document outline tree. PDF 32000-1:2008 §12.3.3 — Document outline.

```csharp
public sealed class OutlineItem
```

## Constructors

### `OutlineItem(string title, int destinationPageIndex, IReadOnlyList<OutlineItem> children)`

Initialises a new `OutlineItem`.

## Properties

### `Title`

```csharp
string Title
```

Gets the bookmark's display title.

### `DestinationPageIndex`

```csharp
int DestinationPageIndex
```

Gets the zero-based page index the bookmark points to, or -1 when the destination cannot be resolved to a page.

### `Children`

```csharp
IReadOnlyList<OutlineItem> Children
```

Gets the nested child bookmarks, if any.

---

_Source: [`src/Chuvadi.Pdf.Forms/OutlineItem.cs`](../../../src/Chuvadi.Pdf.Forms/OutlineItem.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
