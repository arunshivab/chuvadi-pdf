# PdfDestination

**Class** in `Chuvadi.Pdf.Forms` (Forms)

A resolved destination: the target page, and how the page is shown (3.32.0).

```csharp
public sealed class PdfDestination
```

## Remarks

Coordinates are in the target page's default user space: points, origin at the bottom left, the space of its MediaBox, before `/Rotate`. A null coordinate or zoom means "leave unchanged" (PDF 32000-1:2008 §12.3.2.2), or that `Kind` has no such value.

## Constructors

### `PdfDestination(int pageIndex, PdfDestinationKind kind)`

Initialises a destination.

**Parameters**

- `pageIndex` — The zero-based target page index.
- `kind` — How the page is positioned.

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based target page index. For a remote (`/GoToR`) destination it is the page number as written in the other file.

### `Kind`

```csharp
PdfDestinationKind Kind
```

Gets how the page is positioned.

### `Left`

```csharp
double? Left
```

Gets the left edge, or null.

### `Top`

```csharp
double? Top
```

Gets the top edge, or null.

### `Right`

```csharp
double? Right
```

Gets the right edge (`/FitR` only), or null.

### `Bottom`

```csharp
double? Bottom
```

Gets the bottom edge (`/FitR` only), or null.

### `Zoom`

```csharp
double? Zoom
```

Gets the magnification (`/XYZ` only), or null. Zero also means "unchanged" in the specification and is reported as null.

---

_Source: [`src/Chuvadi.Pdf.Forms/PdfDestination.cs`](../../../src/Chuvadi.Pdf.Forms/PdfDestination.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
