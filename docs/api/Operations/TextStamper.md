# TextStamper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Draws a single line of text at one of twelve anchor positions on selected pages, with template-token substitution (page numbers in several styles, file name/path, caller-supplied date/time, literal text). The stamp is an overlay: existing content is not moved. For running headers/footers that reserve space and reflow content, use `HeaderFooter`. PDF 32000-1:2008 §9.4 — text; §8.10.1 — form XObjects.

```csharp
public static class TextStamper
```

---

_Source: [`src/Chuvadi.Pdf.Operations/TextStamper.cs`](../../../src/Chuvadi.Pdf.Operations/TextStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
