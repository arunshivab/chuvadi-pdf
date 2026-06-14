# OptionalContentWriter

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Writes optional-content (layer) visibility changes to a PDF document.

```csharp
public static class OptionalContentWriter
```

## Remarks

Complements `OptionalContentReader`: read the groups, then toggle any of them on or off by name and write a new document. The change edits the default configuration's (/OCProperties/D) /ON and /OFF arrays; the original document is not modified in place.

---

_Source: [`src/Chuvadi.Pdf.Documents/OptionalContentWriter.cs`](../../../src/Chuvadi.Pdf.Documents/OptionalContentWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
