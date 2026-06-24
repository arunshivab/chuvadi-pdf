# PageSize

**Record** in `Chuvadi.Pdf.Authoring` (Authoring)

A page size in PDF points (1 pt = 1/72 inch).

```csharp
public readonly record struct PageSize(double Width, double Height)
```

## Properties

### `A4`

__static__

```csharp
static PageSize A4
```

A4: 595 × 842 pt (210 × 297 mm).

### `A3`

__static__

```csharp
static PageSize A3
```

A3: 842 × 1191 pt.

### `A5`

__static__

```csharp
static PageSize A5
```

A5: 420 × 595 pt.

### `Letter`

__static__

```csharp
static PageSize Letter
```

US Letter: 612 × 792 pt (8.5 × 11 inch).

### `Legal`

__static__

```csharp
static PageSize Legal
```

US Legal: 612 × 1008 pt (8.5 × 14 inch).

### `Tabloid`

__static__

```csharp
static PageSize Tabloid
```

US Tabloid: 792 × 1224 pt (11 × 17 inch).

## Methods

### `Landscape`

```csharp
PageSize Landscape() => new(Height, Width)
```

Returns a landscape-oriented version of this size (swaps width and height).

---

_Source: [`src/Chuvadi.Pdf.Authoring/PageSize.cs`](../../../src/Chuvadi.Pdf.Authoring/PageSize.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
