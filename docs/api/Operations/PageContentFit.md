# PageContentFit

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

How header/footer bands interact with existing page content.

```csharp
public enum PageContentFit
```

## Values

| Name | Description |
|---|---|
| `Overlay` | Draw the header/footer in the page margins without moving content. Fast, but may overlap content if the margins are not empty. |
| `ReserveAndScale` | Always reserve the header and footer band heights, scaling existing content down uniformly and shifting it to fit the remaining height. Never overlaps; the trade-off is a slight, uniform "zoom out" of content. |
| `ScaleIfIntruding` | Reserve and scale only when existing content actually reaches into a band; otherwise behave like `Overlay`. Closest to a word processor, but band intrusion is detected heuristically. |

---

_Source: [`src/Chuvadi.Pdf.Operations/PageContentFit.cs`](../../../src/Chuvadi.Pdf.Operations/PageContentFit.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
