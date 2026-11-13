# RedactionReport

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

What `Redactor.ApplyWithReport` removed, page by page (3.34.0).

```csharp
public sealed class RedactionReport
```

## Properties

### `Pages`

```csharp
IReadOnlyList<PageRedactionReport> Pages
```

Gets one entry per page that was rewritten (a page with a redaction rectangle or an applicable text pattern), in page order. A page whose counts are all zero had nothing under its rectangles.

### `DocumentMatchesRemoved`

```csharp
int DocumentMatchesRemoved
```

Gets the number of matches of the document-wide patterns (those with no page list) removed outside the pages: from bookmark titles and from the XMP metadata (3.34.0). The redacted file carries no document information dictionary (`/Info`) at all.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionReport.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
