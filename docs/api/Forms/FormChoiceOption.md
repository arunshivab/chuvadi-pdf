# FormChoiceOption

**Class** in `Chuvadi.Pdf.Forms` (Forms)

One option of a choice field (list box or combo box).

```csharp
public sealed class FormChoiceOption
```

## Constructors

### `FormChoiceOption(string exportValue, string displayText)`

Initialises a new `FormChoiceOption`.

**Parameters**

- `exportValue` — The value stored in `/V` when this option is chosen.
- `displayText` — The text shown for the option.

## Properties

### `ExportValue`

```csharp
string ExportValue
```

Gets the value stored in `/V` when this option is chosen.

### `DisplayText`

```csharp
string DisplayText
```

Gets the text shown for the option. Equal to `ExportValue` when the `/Opt` entry is a single string.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormChoiceOption.cs`](../../../src/Chuvadi.Pdf.Forms/FormChoiceOption.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
