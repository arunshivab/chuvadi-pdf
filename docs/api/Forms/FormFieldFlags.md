# FormFieldFlags

**Enum** in `Chuvadi.Pdf.Forms` (Forms)

The field flags (`/Ff`) of a form field. The values are the PDF bit positions, so a raw `/Ff` integer casts directly.

```csharp
public enum FormFieldFlags
```

## Values

| Name | Description |
|---|---|
| `None` | No flag is set. |
| `ReadOnly` | The user may not change the value (bit 1). |
| `Required` | The field must have a value when the form is submitted (bit 2). |
| `NoExport` | The field is not exported when the form is submitted (bit 3). |
| `Multiline` | Text: the field may hold several lines (bit 13). |
| `Password` | Text: the value is a password and is not echoed (bit 14). |
| `NoToggleToOff` | Radio: exactly one button must be on; clicking the on button does not turn it off (bit 15). |
| `Radio` | Button: the field is a set of radio buttons (bit 16). |
| `Pushbutton` | Button: the field is a pushbutton with no value (bit 17). |
| `Combo` | Choice: a combo box rather than a list box (bit 18). |
| `Edit` | Choice: a combo box with an editable text box (bit 19). |
| `Sort` | Choice: the options are sorted (bit 20). |
| `FileSelect` | Text: the field selects a file to submit (bit 21). |
| `MultiSelect` | Choice: more than one option may be selected (bit 22). |
| `DoNotSpellCheck` | Text and choice: no spell checking (bit 23). |
| `DoNotScroll` | Text: no scrolling beyond the visible area (bit 24). |
| `Comb` | Text: the field is divided into `/MaxLen` equal cells (bit 25). |
| `RadiosInUnison` | Bit 26. Radio: buttons with the same on-state turn on and off together. On a text field the same bit means the value is rich text. |
| `CommitOnSelChange` | Choice: a new value is committed as soon as a selection is made (bit 27). |

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFieldFlags.cs`](../../../src/Chuvadi.Pdf.Forms/FormFieldFlags.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
