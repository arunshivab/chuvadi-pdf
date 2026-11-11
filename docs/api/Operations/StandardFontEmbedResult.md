# StandardFontEmbedResult

**Class** in `Chuvadi.Pdf.Operations` (Operations)

What `StandardFontEmbedder.Embed` did.

```csharp
public sealed class StandardFontEmbedResult
```

## Properties

### `EmbeddedFonts`

```csharp
IReadOnlyList<string> EmbeddedFonts
```

Gets the `/BaseFont` names that were embedded, each once, in the order first found.

### `NotEmbedded`

```csharp
IReadOnlyList<string> NotEmbedded
```

Gets one message for each non-embedded font that was left as it was: Symbol and ZapfDingbats (their substitute fonts' licence is not confirmed), composite (Type0) fonts, and fonts that are neither a standard font nor one of its aliases.

---

_Source: [`src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs`](../../../src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
