# ResolvedColorSpace

**Class** in `Chuvadi.Pdf.Content` (Content)

A resolved PDF colour space that converts its colour components to sRGB.

```csharp
public sealed class ResolvedColorSpace
```

## Properties

### `Kind`

```csharp
Family Kind => _kind
```

Gets the colour-space family.

### `ComponentCount`

```csharp
int ComponentCount => _componentCount
```

Gets the number of colour components the space expects.

### `IsPattern`

```csharp
bool IsPattern => _kind == Family.Pattern
```

Gets a value indicating whether this is a Pattern space.

## Methods

### `ToRgb`

```csharp
double[] ToRgb(double[] components)
```

Converts colour components to sRGB. Components shorter than `ComponentCount` are treated as zero; longer inputs ignore the surplus. The result is three channels in [0, 1].

**Parameters**

- `components` — The colour components (sc / scn operands).

**Returns:** An sRGB triple in [0, 1].

### `Parse`

__static__

```csharp
static ResolvedColorSpace? Parse(PdfPrimitive colorSpace, PdfObjectStore objects)
```

Parses a colour-space object: a name (such as `/DeviceRGB`) or an array (such as `[/ICCBased stream]`). Returns `null` when the object cannot be understood.

**Parameters**

- `colorSpace` — The colour-space primitive.
- `objects` — The object store used to resolve references.

**Returns:** The resolved space, or `null`.

---

_Source: [`src/Chuvadi.Pdf.Content/ResolvedColorSpace.cs`](../../../src/Chuvadi.Pdf.Content/ResolvedColorSpace.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
