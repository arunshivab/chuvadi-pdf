# SanitizeReport

**Class** in `Chuvadi.Pdf.Operations` (Operations)

What `PdfSanitizer` found (and, when sanitizing, removed).

```csharp
public sealed class SanitizeReport
```

## Properties

### `ActionsRemoved`

```csharp
int ActionsRemoved
```

Gets the number of actions removed (link, outline, widget, page and document actions, including `/OpenAction`).

### `AdditionalActionsRemoved`

```csharp
int AdditionalActionsRemoved
```

Gets the number of additional-actions dictionaries (`/AA`) removed.

### `JavaScriptTreesRemoved`

```csharp
int JavaScriptTreesRemoved
```

Gets the number of document-level JavaScript name trees removed.

### `EmbeddedFilesRemoved`

```csharp
int EmbeddedFilesRemoved
```

Gets the number of embedded-file entries removed (name trees, `/AF`, `/EF`, `/RF`, portfolios).

### `AnnotationsRemoved`

```csharp
int AnnotationsRemoved
```

Gets the number of annotations removed (file attachments, multimedia).

### `XfaRemoved`

```csharp
bool XfaRemoved
```

Gets a value indicating whether XFA packets were removed.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfSanitizer.cs`](../../../src/Chuvadi.Pdf.Operations/PdfSanitizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
