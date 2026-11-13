# MergeOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options for `PageOperations.Merge(System.IO.Stream, System.Collections.Generic.IReadOnlyList{Chuvadi.Pdf.Documents.PdfDocument}, MergeOptions)`. Controls whether and how each input document's outline (bookmark) tree is carried into the merged output.

```csharp
public sealed class MergeOptions
```

## Properties

### `PreserveOutlines`

```csharp
bool PreserveOutlines
```

Gets or initialises whether each input's outline is carried into the merged output with its destination page indices re-based to the merged page offsets. Bookmarks whose destination cannot be resolved are carried as title-only entries. Default: `false` (no outline, matching the parameterless merge overload).

### `WrapPerDocument`

```csharp
bool WrapPerDocument
```

Gets or initialises whether each input's top-level bookmarks are nested under one synthetic per-document parent node, whose destination is that document's first merged page. Has no effect when `PreserveOutlines` is `false`, and no parent node is emitted for an input that contributes no bookmarks. Default: `false`.

### `DocumentTitles`

```csharp
IReadOnlyList<string?>? DocumentTitles
```

Gets or initialises the titles used for the per-document parent nodes when `WrapPerDocument` is set. Indexed positionally against the merge input list; a null, empty, or missing entry falls back to the document's `Chuvadi.Pdf.Documents.PdfDocument.Title`, then to "Document N" (one-based). Default: `null`.

### `FormFields`

```csharp
MergeFormFields FormFields
```

Gets or initialises how the inputs' form fields are carried (3.34.0, D-335). Default: `MergeFormFields.RenameOnConflict`: every input's fields are kept, and a top-level field whose name is already taken is renamed `name_2`, `name_3`, and so on. Before 3.34.0 a merge dropped the form (the widgets stayed as plain annotations), which is `MergeFormFields.Drop`.

### `Encryption`

```csharp
Chuvadi.Pdf.IO.EncryptionOptions? Encryption
```

Gets or initialises the encryption applied to the merged output, or null (the default) to write it unencrypted (3.34.0, D-335). The inputs' own encryption is never carried over.

---

_Source: [`src/Chuvadi.Pdf.Operations/MergeOptions.cs`](../../../src/Chuvadi.Pdf.Operations/MergeOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
