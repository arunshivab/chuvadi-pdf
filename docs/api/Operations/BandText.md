# BandText

**Class** in `Chuvadi.Pdf.Operations` (Operations)

The left, centre, and right text segments of a header or footer band. Each segment is an independent template (may contain tokens); a null segment is omitted.

```csharp
public sealed class BandText
```

## Constructors

### `BandText(string? left = null, string? center = null, string? right = null)`

Initialises a band with optional left/centre/right segments.

**Parameters**

- `left` — Left-aligned segment template, or null.
- `center` — Centre-aligned segment template, or null.
- `right` — Right-aligned segment template, or null.

## Properties

### `Left`

```csharp
string? Left
```

Gets the left-aligned segment template, or null.

### `Center`

```csharp
string? Center
```

Gets the centre-aligned segment template, or null.

### `Right`

```csharp
string? Right
```

Gets the right-aligned segment template, or null.

---

_Source: [`src/Chuvadi.Pdf.Operations/HeaderFooterOptions.cs`](../../../src/Chuvadi.Pdf.Operations/HeaderFooterOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
