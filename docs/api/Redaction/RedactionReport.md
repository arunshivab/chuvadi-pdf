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

Gets the number of matches of the document-wide patterns (those with no page list) removed outside the pages: from bookmark titles and from the XMP metadata (3.34.0), and from URI actions and alternate texts (`/ActualText`, `/Alt`, `/E`) in the document's objects (3.35.0). The redacted file carries no document information dictionary (`/Info`) at all. Scripts are counted in `ScriptsRemoved` and attachments in `AttachmentsRemoved`.

### `CatalogEntriesRemoved`

```csharp
IReadOnlyList<string> CatalogEntriesRemoved
```

Gets the document catalog entries left out of the redacted file because the objects they refer to were not written (3.35.0): the name trees other than named destinations, attachments and JavaScript (for example `Names/Templates`, page templates whose content is outside the page tree), and inside the form `AcroForm/XFA` and `AcroForm/Fields n 0 R` for fields with no widget on a page. They are dropped rather than carried through unscrubbed. Named destinations, attachments, JavaScript, `OpenAction` and `AA` are kept (see `AttachmentsRemoved` and `ScriptsRemoved`). Empty when nothing was dropped.

### `AttachmentsRemoved`

```csharp
IReadOnlyList<string> AttachmentsRemoved
```

Gets the names of the attachments (embedded files) removed (3.35.0): those that matched a document-wide pattern, in their content or their name, and, in a pattern redaction unless `RedactionOptions.KeepAttachments` is set, those that could not be checked (not text, or only page-limited patterns). An attachment whose name matched is listed as `(name redacted)`. The attachment's file data is not in the redacted file. Empty when none was removed.

### `AttachmentsNotScanned`

```csharp
IReadOnlyList<string> AttachmentsNotScanned
```

Gets the names of the attachments kept without being checked against the patterns (3.35.0): with rectangles only (no patterns), every attachment; in a pattern redaction with `RedactionOptions.KeepAttachments` set, those that could not be checked. Their content is carried to the redacted file as it was.

### `ScriptsRemoved`

```csharp
int ScriptsRemoved
```

Gets the number of JavaScript actions whose script matched a document-wide pattern (or could not be read) and was removed (3.35.0). The action itself is kept with an empty script; document JavaScript that does not match is kept as it was.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionReport.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
