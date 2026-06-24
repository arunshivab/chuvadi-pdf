# TableRenderResult

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Outcome of rendering a table; may contain overflow rows.

```csharp
public sealed class TableRenderResult
```

## Properties

### `HasOverflow`

```csharp
bool HasOverflow
```

True when not all rows fit on the page.

### `RemainingRows`

```csharp
IReadOnlyList<string[]> RemainingRows
```

The rows that didn't fit. Empty when `HasOverflow` is false.

### `NextYFromTop`

```csharp
double NextYFromTop
```

Y position immediately below the last drawn row.

---

_Source: [`src/Chuvadi.Pdf.Authoring/TableBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/TableBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
