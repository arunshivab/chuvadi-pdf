# DocumentInfo

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Sets document-information metadata (Title, Author, Subject, Keywords) on an existing document and writes the result. The rest of the document — pages, outlines, resources — is preserved unchanged. A null argument leaves the corresponding entry untouched; passing an empty string clears it. PDF 32000-1:2008 §14.3.3 — Document information dictionary.

```csharp
public static class DocumentInfo
```

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, string? title = null, string? author = null, string? subject = null, string? keywords = null)
```

Writes `document` to `output` with the given information-dictionary entries applied. Any parameter left null is preserved from the source document; a non-null value (including the empty string) replaces it.

**Parameters**

- `output` — The stream to write the updated PDF to.
- `document` — The source document.
- `title` — New /Title, or null to leave unchanged.
- `author` — New /Author, or null to leave unchanged.
- `subject` — New /Subject, or null to leave unchanged.
- `keywords` — New /Keywords, or null to leave unchanged. <exception cref="ArgumentNullException"> Thrown when `output` or `document` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/DocumentInfo.cs`](../../../src/Chuvadi.Pdf.Operations/DocumentInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
