# ReportPageSetup

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Page geometry for a report: paper size and the four margins.

```csharp
public sealed class ReportPageSetup
```

## Properties

### `Default`

__static__

```csharp
static ReportPageSetup Default
```

Default setup: A4 portrait with 50-point margins.

### `PageSize`

```csharp
PageSize PageSize
```

Gets or initialises the paper size. Default: A4. Use `PageSize.Landscape` for landscape.

### `MarginLeft`

```csharp
double MarginLeft
```

Gets or initialises the left margin in points. Default: 50.

### `MarginTop`

```csharp
double MarginTop
```

Gets or initialises the top margin in points. Default: 50.

### `MarginRight`

```csharp
double MarginRight
```

Gets or initialises the right margin in points. Default: 50.

### `MarginBottom`

```csharp
double MarginBottom
```

Gets or initialises the bottom margin in points. Default: 50.

### `ContentWidth`

```csharp
double ContentWidth => PageSize.Width - MarginLeft - MarginRight
```

The width of the content area between the side margins.

### `ContentHeight`

```csharp
double ContentHeight => PageSize.Height - MarginTop - MarginBottom
```

The height of the content area between the top and bottom margins.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
