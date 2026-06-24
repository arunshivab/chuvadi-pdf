# EmbeddedFontObjects

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

The result of embedding a TrueType font: the top-level Type0 font object id to reference from a page's `/Font` resource, and every object that must be added to the document.

```csharp
public sealed class EmbeddedFontObjects
```

## Constructors

### `EmbeddedFontObjects(PdfObjectId type0FontId, IReadOnlyList<PdfIndirectObject> objects)`

Initialises a new `EmbeddedFontObjects`.

**Parameters**

- `type0FontId` — The top-level Type0 font object id.
- `objects` — All objects to add to the document.

## Properties

### `Type0FontId`

```csharp
PdfObjectId Type0FontId
```

Gets the Type0 font object id to reference from page resources.

### `Objects`

```csharp
IReadOnlyList<PdfIndirectObject> Objects
```

Gets every object to add to the document.

---

_Source: [`src/Chuvadi.Pdf.Authoring/EmbeddedFontObjects.cs`](../../../src/Chuvadi.Pdf.Authoring/EmbeddedFontObjects.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
