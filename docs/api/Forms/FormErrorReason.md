# FormErrorReason

**Enum** in `Chuvadi.Pdf.Forms` (Forms)

Why `FormFiller` refused a value (`FormException.Reason`).

```csharp
public enum FormErrorReason
```

## Values

| Name | Description |
|---|---|
| `UnknownState` | A check box or radio-group value matches none of the field's states. |
| `NotInOptions` | With `FormFillOptions.Validate`, a choice value is not one of the field's option export values (an editable combo box accepts any value). |
| `TooLong` | With `FormFillOptions.Validate`, a text value is longer than the field's maximum length (counted in Unicode code points). |
| `TooManyValues` | More than one value was given for a field that holds one: anything but a multi-select list box. |
| `UnknownField` | With `FormUnknownFields.Throw`, a name matches no field. |

---

_Source: [`src/Chuvadi.Pdf.Forms/FormErrorReason.cs`](../../../src/Chuvadi.Pdf.Forms/FormErrorReason.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
