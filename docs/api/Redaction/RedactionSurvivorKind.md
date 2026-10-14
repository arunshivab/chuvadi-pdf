# RedactionSurvivorKind

**Enum** in `Chuvadi.Pdf.Redaction` (Redaction)

The kind of content found inside a region.

```csharp
public enum RedactionSurvivorKind
```

## Values

| Name | Description |
|---|---|
| `Text` | A visible glyph (its approximate ink box meets the region). |
| `Image` | A painted image whose placement meets the region. |
| `Path` | A painted path (fill or stroke ink) that meets the region. |
| `Annotation` | An annotation whose rectangle meets the region. |

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
