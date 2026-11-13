# FormFillOptions

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Options for `FormFiller`.

```csharp
public sealed class FormFillOptions
```

## Properties

### `Encryption`

```csharp
EncryptionOptions? Encryption
```

Gets the encryption to apply to the filled document, or null (the default) to write it unencrypted. A source opened with a password is decrypted in memory, so without this option the filled copy is not encrypted; pass the encryption again to keep the form protected.

### `UnknownFields`

```csharp
FormUnknownFields UnknownFields
```

Gets what happens to a name that matches no field (3.34.0). The default, `FormUnknownFields.Ignore`, is the behaviour before 3.34.0; it may become `FormUnknownFields.Throw` in 4.0.

### `Validate`

```csharp
bool Validate
```

Gets whether values are checked against the field before they are written (3.34.0). When true, a choice value that is not one of the field's option export values throws a `FormException` with `FormErrorReason.NotInOptions` (an editable combo box accepts any value), and a text value longer than the field's maximum length, counted in Unicode code points, throws one with `FormErrorReason.TooLong`; an empty value always clears the field. When false (the default in 3.x; it may become true in 4.0), values are written exactly as given. In neither mode is a value truncated or changed.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFillOptions.cs`](../../../src/Chuvadi.Pdf.Forms/FormFillOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
