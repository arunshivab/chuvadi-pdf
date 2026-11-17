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
| `Image` | A painted image whose placement meets the region and whose pixels there are not blanked (a partly redacted image with only blank pixels inside the region is not reported). |
| `Path` | A painted path (fill or stroke ink) that meets the region. |
| `Annotation` | An annotation whose rectangle meets the region, or (for `RedactionVerifier.VerifyPatterns`) whose appearance or text entries match a pattern. |
| `DocumentText` | Text outside the page content that matches a pattern (3.35.0): a string anywhere in the document (bookmark title, document information, action, name, alternate text), the XMP metadata, a script, or replacement text in a content stream. Reported by `RedactionVerifier.VerifyPatterns` with page index -1. |
| `Attachment` | An attachment (embedded file) that matches a pattern, or that cannot be read as text and so cannot be checked (then `RedactionSurvivor.Text` is null) (3.35.0). Reported by `RedactionVerifier.VerifyPatterns` with page index -1. |
| `Shading` | A shading painted (with `sh`, or as a shading-pattern fill) that meets the region (3.35.0). |

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
