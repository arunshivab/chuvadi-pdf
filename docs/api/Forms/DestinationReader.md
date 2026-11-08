# DestinationReader

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Reads a document's named destinations (3.32.0).

```csharp
public static class DestinationReader
```

## Methods

### `GetNamedDestinations`

__static__

```csharp
static IReadOnlyDictionary<string, PdfDestination> GetNamedDestinations(PdfDocument document)
```

Returns the document's named destinations, keyed by name: the `/Names /Dests` name tree first, then the catalog's legacy `/Dests` dictionary (a name in both keeps the name-tree entry). Names whose destination cannot be resolved to a page are left out.

**Parameters**

- `document` — The document.

**Returns:** The named destinations; empty when there are none.

**Remarks:** Keys are the names as raw bytes read as Latin-1, the form `/GoTo` actions use to refer to them.

---

_Source: [`src/Chuvadi.Pdf.Forms/DestinationReader.cs`](../../../src/Chuvadi.Pdf.Forms/DestinationReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
