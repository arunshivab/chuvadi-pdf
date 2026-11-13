# FormFillResult

**Class** in `Chuvadi.Pdf.Forms` (Forms)

What `FormFiller` did: the fields it wrote and, with `FormUnknownFields.Report`, the names that matched no field.

```csharp
public sealed class FormFillResult
```

## Properties

### `FilledFields`

```csharp
IReadOnlyList<string> FilledFields
```

Gets the names of the fields whose value was written, in the order the values were given. A pushbutton (which has no value) is not listed.

### `UnknownFields`

```csharp
IReadOnlyList<string> UnknownFields
```

Gets the names that matched no field, in the order given, when `FormFillOptions.UnknownFields` is `FormUnknownFields.Report`; otherwise empty.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFillResult.cs`](../../../src/Chuvadi.Pdf.Forms/FormFillResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
