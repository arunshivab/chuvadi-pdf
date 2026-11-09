# OptionalContentGroup

**Class** in `Chuvadi.Pdf.Documents` (Documents)

An Optional Content Group (OCG) — a named, toggleable layer in a PDF.

```csharp
public sealed class OptionalContentGroup
```

## Remarks

PDF 32000 §8.11 defines optional content as graphics that can be selectively shown or hidden by the viewer. Common uses: anatomical overlays in medical imaging, engineering drawing layers, multi-language annotation sets.

## Constructors

### `OptionalContentGroup(string name, bool isVisibleByDefault, IReadOnlyList<string> intents)`

Initialises a new `OptionalContentGroup`.

## Properties

### `Name`

```csharp
string Name
```

Gets the human-readable layer name (from /Name).

### `IsVisibleByDefault`

```csharp
bool IsVisibleByDefault
```

Gets whether the layer is visible in the default configuration. Computed from the document's /D/ON and /D/OFF arrays.

### `Intents`

```csharp
IReadOnlyList<string> Intents
```

Gets the layer's intents (e.g., "View", "Design"). Empty when unspecified.

### `Id`

```csharp
int Id
```

Gets the layer's id (3.32.0): its object number, unique within the document, so layers with the same name can be told apart; -1 for a group written directly in `/OCGs` instead of by reference. Use it with `OptionalContentVisibility.WithOverridesById`. It identifies the layer in this opened document; another tool rewriting the file may renumber it.

### `IsLocked`

```csharp
bool IsLocked
```

Gets whether the default configuration locks the layer (3.32.0): it is in `/OCProperties /D /Locked`, so a viewer does not let its user change the layer's state (PDF 32000-1:2008 §8.11.4.3). Chuvadi's overrides still apply to it.

---

_Source: [`src/Chuvadi.Pdf.Documents/OptionalContentGroup.cs`](../../../src/Chuvadi.Pdf.Documents/OptionalContentGroup.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
