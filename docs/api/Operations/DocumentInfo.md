# DocumentInfo

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Sets document-information metadata (Title, Author, Subject, Keywords) on an existing document and writes the result. The rest of the document — pages, outlines, resources — is preserved unchanged. A null argument leaves the corresponding entry untouched; passing an empty string clears it. PDF 32000-1:2008 §14.3.3 — Document information dictionary.

```csharp
public static class DocumentInfo
```

---

_Source: [`src/Chuvadi.Pdf.Operations/DocumentInfo.cs`](../../../src/Chuvadi.Pdf.Operations/DocumentInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
