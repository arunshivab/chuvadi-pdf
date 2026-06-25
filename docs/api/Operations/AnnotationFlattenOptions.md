# AnnotationFlattenOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options controlling `AnnotationFlattener`: which annotation kinds to bake, whether to strip the AcroForm field tree once its widgets are baked, whether to skip invisible annotations, and whether to drop any annotations left live after baking.

```csharp
public sealed class AnnotationFlattenOptions
```

## Properties

### `Kinds`

```csharp
AnnotationFlattenKinds Kinds
```

Which annotation kinds to bake into page content. Defaults to `AnnotationFlattenKinds.All`.

### `RemoveAcroForm`

```csharp
bool RemoveAcroForm
```

When `AnnotationFlattenKinds.FormFields` are flattened and no widget had to be left live, removes the catalog's `/AcroForm` entry so the output is no longer an interactive form. Defaults to `true`.

### `SkipHiddenAndNoView`

```csharp
bool SkipHiddenAndNoView
```

Skips baking annotations flagged Hidden or NoView (they paint nothing), and removes them from the page's `/Annots`. Defaults to `true`.

### `DropRemainingAnnotations`

```csharp
bool DropRemainingAnnotations
```

After baking, removes every annotation still live — unselected kinds and any selected annotation that could not be baked (e.g. links with no appearance) — leaving a fully static page. Defaults to `false`, which keeps those annotations interactive.

### `Default`

__static__

```csharp
static AnnotationFlattenOptions Default
```

Gets the default options: bake all kinds, strip a fully-baked AcroForm, skip invisible, keep un-baked.

---

_Source: [`src/Chuvadi.Pdf.Operations/AnnotationFlattenOptions.cs`](../../../src/Chuvadi.Pdf.Operations/AnnotationFlattenOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
