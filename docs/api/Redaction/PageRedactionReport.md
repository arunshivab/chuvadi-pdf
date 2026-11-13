# PageRedactionReport

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

The redactions made on one page. Content drawn through a form XObject counts on the first page that draws it.

```csharp
public sealed class PageRedactionReport
```

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based page index.

### `GlyphsRemoved`

```csharp
int GlyphsRemoved
```

Gets the number of glyphs removed from text (each replaced by a gap of the same width).

### `ImagesBlanked`

```csharp
int ImagesBlanked
```

Gets the number of image placements whose covered pixels were blanked (the rest of the image kept).

### `ImagesRemoved`

```csharp
int ImagesRemoved
```

Gets the number of image placements removed whole.

### `PathsCut`

```csharp
int PathsCut
```

Gets the number of painted paths that had the redacted area cut out of them.

### `PathsRemoved`

```csharp
int PathsRemoved
```

Gets the number of painted paths removed whole.

### `AnnotationsRemoved`

```csharp
int AnnotationsRemoved
```

Gets the number of annotations (other than form widgets) removed.

### `FieldValuesCleared`

```csharp
int FieldValuesCleared
```

Gets the number of form-field widgets whose field value was cleared.

### `AlternateTextsRemoved`

```csharp
int AlternateTextsRemoved
```

Gets the number of alternate texts (`/ActualText`, `/Alt`, `/E`) removed because their content was redacted, in marked content and in the structure tree.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionReport.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
