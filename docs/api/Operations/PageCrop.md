# PageCrop

**Struct** in `Chuvadi.Pdf.Operations` (Operations)

Identifies a single page to crop and the crop rectangle, in PDF user-space points (origin at the bottom-left of the page), to confine it to.

```csharp
public readonly struct PageCrop : IEquatable<PageCrop>
```

## Constructors

### `PageCrop(int pageIndex, RectangleF cropBox)`

Initializes a new `PageCrop`.

**Parameters**

- `pageIndex` — The zero-based index of the page to crop.
- `cropBox` — The crop rectangle in PDF user-space points.

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based index of the page to crop.

### `CropBox`

```csharp
RectangleF CropBox
```

Gets the crop rectangle in PDF user-space points.

## Methods

### `Equals`

```csharp
bool Equals(PageCrop other) => PageIndex == other.PageIndex && CropBox.Equals(other.CropBox)
```

Determines whether this value equals `other`.

**Parameters**

- `other` — The value to compare with.

**Returns:** `true` when both values are equal.

### `Equals`

```csharp
override bool Equals(object? obj) => obj is PageCrop other && Equals(other)
```

Determines whether this value equals `obj`.

**Parameters**

- `obj` — The object to compare with.

**Returns:** `true` when `obj` is an equal `PageCrop`.

### `GetHashCode`

```csharp
override int GetHashCode() => HashCode.Combine(PageIndex, CropBox)
```

Returns a hash code for this value.

**Returns:** A hash code combining the page index and crop rectangle.

### `==`

__static__

```csharp
static bool operator ==(PageCrop left, PageCrop right) => left.Equals(right)
```

Determines whether two `PageCrop` values are equal.

**Parameters**

- `left` — The left value.
- `right` — The right value.

**Returns:** `true` when the values are equal.

### `!=`

__static__

```csharp
static bool operator !=(PageCrop left, PageCrop right) => !left.Equals(right)
```

Determines whether two `PageCrop` values are not equal.

**Parameters**

- `left` — The left value.
- `right` — The right value.

**Returns:** `true` when the values are not equal.

---

_Source: [`src/Chuvadi.Pdf.Operations/PageCrop.cs`](../../../src/Chuvadi.Pdf.Operations/PageCrop.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
