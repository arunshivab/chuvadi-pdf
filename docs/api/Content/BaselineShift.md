# BaselineShift

**Enum** in `Chuvadi.Pdf.Content` (Content)

Whether a `TextFragment` sits on the baseline of the line it belongs to, or is raised (a superscript) or lowered (a subscript) off it.

```csharp
public enum BaselineShift
```

## Remarks

Set by text extraction (Chuvadi.Pdf.Text) from the fragment's size and position against the main text of its line; the content-stream parser alone reports `None`. Lets a consumer render real superscripts and subscripts (for example HTML `&lt;sup&gt;` and `&lt;sub&gt;`) without inspecting characters.

## Values

| Name | Description |
|---|---|
| `None` | On the line's baseline, or not classified. |
| `Raised` | Smaller than the line's main text and raised above its baseline. |
| `Lowered` | Smaller than the line's main text and lowered below its baseline. |

---

_Source: [`src/Chuvadi.Pdf.Content/BaselineShift.cs`](../../../src/Chuvadi.Pdf.Content/BaselineShift.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
