# AnnotationAppearance

**Class** in `Chuvadi.Pdf.Documents` (Documents)

A page annotation's resolved normal appearance and its placement on the page, per PDF 32000-1:2008 §12.5.5.

```csharp
public sealed class AnnotationAppearance
```

## Remarks

The placement maps a point already transformed by the appearance form's own `/Matrix` into page space: `x' = ScaleX * x + OffsetX`, `y' = ScaleY * y + OffsetY`. Consumers that replay the appearance content stream should first apply this placement to the current transformation matrix and then invoke the form (whose own `/Matrix` and `/Resources` apply as for any form XObject).

## Properties

### `Annotation`

```csharp
PdfDictionary Annotation
```

The annotation dictionary the appearance belongs to.

### `Appearance`

```csharp
PdfStream Appearance
```

The resolved normal appearance stream (`/AP /N`). When the normal appearance is a state dictionary, the stream selected by the annotation's `/AS` entry.

### `Rect`

```csharp
PdfRectangle Rect
```

The annotation rectangle (`/Rect`) in page space.

### `ScaleX`

```csharp
double ScaleX
```

Horizontal placement scale (§12.5.5 algorithm).

### `ScaleY`

```csharp
double ScaleY
```

Vertical placement scale (§12.5.5 algorithm).

### `OffsetX`

```csharp
double OffsetX
```

Horizontal placement translation (§12.5.5 algorithm).

### `OffsetY`

```csharp
double OffsetY
```

Vertical placement translation (§12.5.5 algorithm).

### `MatrixA`

```csharp
double MatrixA
```

The appearance form's `/Matrix` component a (default 1).

### `MatrixB`

```csharp
double MatrixB
```

The appearance form's `/Matrix` component b (default 0).

### `MatrixC`

```csharp
double MatrixC
```

The appearance form's `/Matrix` component c (default 0).

### `MatrixD`

```csharp
double MatrixD
```

The appearance form's `/Matrix` component d (default 1).

### `MatrixE`

```csharp
double MatrixE
```

The appearance form's `/Matrix` component e (default 0).

### `MatrixF`

```csharp
double MatrixF
```

The appearance form's `/Matrix` component f (default 0).

### `Resources`

```csharp
PdfDictionary? Resources
```

The appearance form's resolved `/Resources` dictionary, or null when the form declares none.

---

_Source: [`src/Chuvadi.Pdf.Documents/AnnotationAppearance.cs`](../../../src/Chuvadi.Pdf.Documents/AnnotationAppearance.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
