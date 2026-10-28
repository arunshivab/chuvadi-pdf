# PdfSanitizer

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Writes a disarmed copy of a document: no JavaScript, no actions that run on open or on any event, no launch, import, submit or remote actions, no embedded files, no XFA scripts and no multimedia — only what is needed to show the pages. In-document navigation (`GoTo`, the four page-turning named actions, threads, layer toggles, transitions, hide) is kept.

```csharp
public static class PdfSanitizer
```

## Remarks

Rendering and text extraction never execute any of this content (see docs/EMBEDDING.md); sanitizing protects whoever opens the output in another viewer. The output is a full rewrite: digital signatures no longer validate, and the file is written unencrypted.

## Methods

### `Sanitize`

__static__

```csharp
static SanitizeReport Sanitize(PdfDocument document, Stream output, SanitizeOptions? options = null)
```

Writes a disarmed copy of `document` to `output`.

**Parameters**

- `document` — The document to sanitize. It is not changed.
- `output` — The stream to write the sanitized PDF to.
- `options` — What to remove; null for the defaults (everything).

**Returns:** What was removed. <exception cref="ArgumentNullException">`document` or `output` is null.</exception> <exception cref="OperationsException"> The document is encrypted and `SanitizeOptions.AllowEncryptedInput` is false. </exception>

### `Inspect`

__static__

```csharp
static SanitizeReport Inspect(PdfDocument document, SanitizeOptions? options = null)
```

Reports the active content in `document` without changing or writing anything: what `Sanitize` would remove with the same options.

**Parameters**

- `document` — The document to inspect.
- `options` — What counts as active; null for the defaults.

**Returns:** What was found.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfSanitizer.cs`](../../../src/Chuvadi.Pdf.Operations/PdfSanitizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
