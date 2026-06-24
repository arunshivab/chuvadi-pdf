# TrueTypeFontEmbedder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Builds the PDF object graph that embeds a TrueType (`glyf`) font as a composite Type0 font with Identity-H encoding, so authored content can draw text in a custom font. The whole font program is embedded (no subsetting in this version); the `/W` width array and `/ToUnicode` CMap cover only the glyphs actually used.

```csharp
public static class TrueTypeFontEmbedder
```

## Remarks

The font is referenced from a page's `/Font` resource by the returned `EmbeddedFontObjects.Type0FontId`. Text drawn with it must be encoded as two-byte big-endian glyph identifiers (the `ContentStreamWriter` handles that). This embeds glyphs in logical order; it does not perform complex-script shaping (GSUB/GPOS or reordering), so Latin renders correctly and Indic renders correctly only for isolated or already-ordered glyphs.

---

_Source: [`src/Chuvadi.Pdf.Authoring/TrueTypeFontEmbedder.cs`](../../../src/Chuvadi.Pdf.Authoring/TrueTypeFontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
