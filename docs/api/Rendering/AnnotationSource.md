# AnnotationSource

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The annotation an op was drawn from (3.31).

```csharp
public sealed record AnnotationSource(PdfDictionary Dictionary, string Subtype, int Flags, string? FieldName, bool Generated)
```

## Parameters

- `Dictionary` — The annotation dictionary.
- `Subtype` — Its `/Subtype`, for example "Widget", "Highlight" or "Stamp".
- `Flags` — Its `/F` flags.
- `FieldName` — For a widget, the field's full name (parts joined with "."); otherwise null.
- `Generated` — True when the appearance was made from the annotation's entries rather than taken from its `/AP`.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
