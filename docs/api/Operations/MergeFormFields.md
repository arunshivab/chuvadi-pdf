# MergeFormFields

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

How `PageOperations.Merge(System.IO.Stream, System.Collections.Generic.IReadOnlyList{Chuvadi.Pdf.Documents.PdfDocument}, MergeOptions)` carries the inputs' form fields (`MergeOptions.FormFields`).

```csharp
public enum MergeFormFields
```

## Remarks

With every mode except `Drop`, the merged form also gets the inputs' default resources (`/DR`; a resource name used by two inputs for different resources is renamed `name_2` in the later input, and its `/DA` strings follow), `NeedAppearances` and `SigFlags` (set when any input sets them), and the calculation order (`/CO`, concatenated). XFA forms are not carried: the merged form is AcroForm only.

## Values

| Name | Description |
|---|---|
| `Drop` | No form: the merged document has no `/AcroForm`, and the widgets stay on their pages as plain annotations (the behaviour before 3.34.0). |
| `RenameOnConflict` | Every input's fields are kept as separate fields. A top-level field whose name an earlier input already uses is renamed `name_2` (then `name_3`, ...), so filling one never changes the other. |
| `ShareSameName` | Fields with the same fully qualified name and type become one field with the widgets of both, so they show the same value (the earlier input's). Parents with the same name are merged: a child both have is shared the same way, and a child only the later input has moves under the earlier parent, keeping what it inherited. Where a top-level field's tree cannot be shared (a type differs, or a field with children meets one without) the later top-level field is renamed as with `RenameOnConflict`. (Before the 3.34.0 release, a field with named children was always renamed.) |

---

_Source: [`src/Chuvadi.Pdf.Operations/MergeFormFields.cs`](../../../src/Chuvadi.Pdf.Operations/MergeFormFields.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
