# RedactionRect

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

One rectangle of content to permanently remove from a PDF page.

```csharp
public sealed class RedactionRect
```

## Remarks

Rectangle coordinates are in PDF user space (PDF points, bottom-left origin). Any text-showing operator (Tj, TJ, ', '') whose visible position falls inside these bounds will be removed from the content stream and the area overpainted with an opaque rectangle.

## Constructors

### `RedactionRect(int pageIndex, RectangleF bounds, string? replacementText = null)`

Initialises a new `RedactionRect`.

**Parameters**

- `pageIndex` — Zero-based page index.
- `bounds` — Rectangle in PDF user space, bottom-left origin.
- `replacementText` — Optional text drawn in place of the removed glyphs, in the same font. When supplied, the matched glyphs are physically removed and this string is rendered in the gap they left, and no opaque box is painted over the region. The replacement must be no wider than the removed span; a wider replacement is rejected with a `RedactionException`.

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based page index targeted by this redaction.

### `Bounds`

```csharp
RectangleF Bounds
```

Gets the rectangle to redact, in PDF user space.

### `ReplacementText`

```csharp
string? ReplacementText
```

Gets the optional in-place replacement text for this region, or `null` to remove the glyphs and paint an opaque box.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionRect.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionRect.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
