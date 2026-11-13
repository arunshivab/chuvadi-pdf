# FormUnknownFields

**Enum** in `Chuvadi.Pdf.Forms` (Forms)

What `FormFiller` does with a name that matches no field (`FormFillOptions.UnknownFields`).

```csharp
public enum FormUnknownFields
```

## Values

| Name | Description |
|---|---|
| `Ignore` | The name is ignored, as before 3.34.0 (the default in 3.x). |
| `Report` | The name is listed in `FormFillResult.UnknownFields`. Only the overload that returns a `FormFillResult` can report; the others throw `System.ArgumentException`. |
| `Throw` | A `FormException` with `FormErrorReason.UnknownField` is thrown before anything is written. |

---

_Source: [`src/Chuvadi.Pdf.Forms/FormUnknownFields.cs`](../../../src/Chuvadi.Pdf.Forms/FormUnknownFields.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
