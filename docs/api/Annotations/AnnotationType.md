# AnnotationType

**Enum** in `Chuvadi.Pdf.Annotations` (Annotations)

PDF annotation subtype. PDF 32000-1:2008 §12.5.6 defines a larger list; Chuvadi models the subtypes most relevant to clinical and document-review workflows. Other subtypes load as `Unknown` but preserve their basic geometry and contents.

```csharp
public enum AnnotationType
```

## Values

| Name | Description |
|---|---|
| `Unknown` | An annotation subtype not modelled by Chuvadi. |
| `Text` | Sticky-note text annotation (§12.5.6.4). |
| `Link` | Hyperlink annotation (§12.5.6.5). |
| `FreeText` | Free-text annotation drawn directly on the page (§12.5.6.6). |
| `Highlight` | Highlight markup annotation (§12.5.6.10). |
| `Underline` | Underline markup annotation (§12.5.6.10). |
| `Squiggly` | Squiggly underline markup annotation (§12.5.6.10). |
| `StrikeOut` | Strike-out markup annotation (§12.5.6.10). |
| `Stamp` | Rubber-stamp annotation, e.g., "Approved" (§12.5.6.12). |
| `Ink` | Free-hand ink annotation (§12.5.6.13). |
| `Square` | Square (rectangle outline) shape annotation (§12.5.6.8). |
| `Circle` | Circle (ellipse outline) shape annotation (§12.5.6.8). |
| `Line` | Line shape annotation (§12.5.6.7). |
| `Polygon` | Polygon (closed shape) annotation (§12.5.6.9). |
| `PolyLine` | PolyLine (open shape) annotation (§12.5.6.9). |

---

_Source: [`src/Chuvadi.Pdf.Annotations/AnnotationType.cs`](../../../src/Chuvadi.Pdf.Annotations/AnnotationType.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
