# FontEmbeddingVerdict

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The whole-document answer of `FontEmbeddingReport`.

```csharp
public enum FontEmbeddingVerdict
```

## Values

| Name | Description |
|---|---|
| `AllEmbedded` | Every font the document can use is embedded (Type 3 fonts count as embedded). |
| `NotAllEmbedded` | At least one font is not embedded; see `FontEmbeddingReport.NotEmbedded`. |
| `Unknown` | Part of the document could not be read, so the answer is not known. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
