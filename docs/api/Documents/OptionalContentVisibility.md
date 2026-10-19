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

### `WithOverrides`

```csharp
OptionalContentVisibility WithOverrides(IReadOnlyDictionary<string, bool>? layerVisibility)
```

Returns a copy with layers overridden by name (null or empty keeps this visibility). Overrides replace any earlier ones.

**Parameters**

- `layerVisibility` — Layer name → visible.

**Returns:** The visibility with the overrides applied.

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
