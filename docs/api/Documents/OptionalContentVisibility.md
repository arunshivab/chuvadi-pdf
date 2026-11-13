# OptionalContentVisibility

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Decides whether optional content (layers) is visible: the document's default configuration (`/OCProperties /D`: `/BaseState`, `/ON`, `/OFF`, and the `/AS` auto-state for viewing), optionally overridden per layer name. Renderers and text extraction use it to skip content in hidden layers, as Acrobat does.

```csharp
public sealed class OptionalContentVisibility
```

## Remarks

Content is tied to a layer by a `BDC /OC` marked-content sequence or by an `/OC` entry on a form XObject, image XObject or annotation. The entry is an optional content group (OCG) or a membership dictionary (OCMD) combining several groups through `/P` (`AnyOn` — the default — `AllOn`, `AnyOff`, `AllOff`) or a `/VE` visibility expression (`And`, `Or`, `Not`). Anything that cannot be resolved is treated as visible, as the specification requires.

## Properties

### `AllVisible`

__static__

```csharp
static OptionalContentVisibility AllVisible
```

Gets a visibility that shows every layer (optional content ignored).

### `ShowsEverything`

```csharp
bool ShowsEverything => _all
```

Gets whether this visibility shows every layer regardless of state.

## Methods

### `FromDocument`

__static__

```csharp
static OptionalContentVisibility FromDocument(PdfDocument document, IReadOnlyDictionary<string, bool>? layerVisibility = null)
```

Builds the visibility of `document`'s default configuration, optionally overriding layers by name.

**Parameters**

- `document` — The document.
- `layerVisibility` — Layer name → visible. Applies to every group with that name; layers not listed keep their default-configuration state. May be null.

**Returns:** The visibility. <exception cref="ArgumentNullException">Thrown when `document` is null.</exception>

### `WithOverrides`

```csharp
OptionalContentVisibility WithOverrides(IReadOnlyDictionary<string, bool>? layerVisibility)
```

Returns a copy with layers overridden by name (null or empty keeps this visibility). Overrides replace any earlier name overrides; id overrides (`WithOverridesById`) are kept and win for their groups. Overrides apply to locked layers too.

**Parameters**

- `layerVisibility` — Layer name → visible.

**Returns:** The visibility with the overrides applied.

### `WithOverridesById`

```csharp
OptionalContentVisibility WithOverridesById(IReadOnlyDictionary<int, bool>? layerVisibility)
```

Returns a copy with layers overridden by id (3.32.0): the `OptionalContentGroup.Id`, the group's object number. Use it to set apart layers that share a name. Null or empty keeps this visibility. Id overrides replace any earlier id overrides, keep the name overrides, and win over a name override for the same group.

**Parameters**

- `layerVisibility` — Layer id → visible.

**Returns:** The visibility with the overrides applied.

**Remarks:** Overrides apply to locked layers too (`OptionalContentGroup.IsLocked`): the lock stops a viewer's user, not the program (D-230).

### `ForPrinting`

```csharp
OptionalContentVisibility ForPrinting()
```

Returns the visibility for printing (3.34.0): the same default configuration and overrides, but the `/AS` auto-state applied is the one for the Print event, so a layer whose `/Usage /Print /PrintState` is `/ON` or `/OFF` takes that state, and the View auto-state is not applied (PDF 32000-1:2008 §8.11.4.4). The renderers and text extraction use it with `AnnotationMode.Print`. A visibility that shows everything, or that is already for printing, is returned as it is.

**Returns:** The visibility for printing.

### `IsLayerVisible`

```csharp
bool IsLayerVisible(string name)
```

Returns whether the layer named `name` is visible (true when no group has that name).

**Parameters**

- `name` — The layer name.

**Returns:** Whether the layer is visible. <exception cref="ArgumentNullException">Thrown when `name` is null.</exception>

### `IsVisible`

```csharp
bool IsVisible(PdfPrimitive? optionalContent)
```

Returns whether content tagged with `optionalContent` (an OCG or OCMD, usually an indirect reference; the value of a `/OC` entry or of a `BDC /OC` property) is visible.

**Parameters**

- `optionalContent` — The optional content entry; null is visible.

**Returns:** Whether the content is visible.

---

_Source: [`src/Chuvadi.Pdf.Documents/OptionalContentVisibility.cs`](../../../src/Chuvadi.Pdf.Documents/OptionalContentVisibility.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
