# PdfWarningKind

**Enum** in `Chuvadi.Pdf.Primitives` (Primitives)

What a `PdfWarning` reports.

```csharp
public enum PdfWarningKind
```

## Values

| Name | Description |
|---|---|
| `FontSubstituted` | An embedded font program could not be read; the text is drawn with the substitute used for a font that is not embedded. |
| `GlyphSkipped` | A glyph outline in an embedded font is damaged; that glyph draws nothing. |
| `ShadingSkipped` | A shading could not be used (unsupported type or damaged entries); its paint is skipped. |
| `AppearanceNotGenerated` | An annotation needed an appearance made from its own entries (a form field with `/NeedAppearances`, or an annotation with no `/AP`), and it could not be made; the annotation draws nothing (3.31). |
| `StreamDamaged` | A stream's data could not be decoded (for example an invalid Flate stream); it is treated as damaged and what it holds (an image, a form, the objects of an object stream) is skipped. The reason says why (3.32.0). |
| `ContentOperatorSkipped` | A content stream operator could not be used and is skipped, for example a path operator with no current point (no `m` before it); the subject names the operator (3.33.0). |

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfWarningKind.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfWarningKind.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
