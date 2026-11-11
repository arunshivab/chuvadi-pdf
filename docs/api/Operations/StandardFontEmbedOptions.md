# StandardFontEmbedOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options for `StandardFontEmbedder`.

```csharp
public sealed class StandardFontEmbedOptions
```

## Properties

### `IncludeAliases`

```csharp
bool IncludeAliases
```

Gets a value indicating whether the common aliases of the standard fonts are embedded too: Arial, Times New Roman and Courier New, in the names Word and other programs write (for example `ArialMT`, `TimesNewRomanPS-BoldMT`). Default true.

### `AllowEncryptedInput`

```csharp
bool AllowEncryptedInput
```

Gets a value indicating whether an encrypted document may be processed. The output is written without encryption. Default false: an encrypted document throws `OperationsException`.

---

_Source: [`src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs`](../../../src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
