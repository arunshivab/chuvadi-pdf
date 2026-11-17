# SignatureModifications

**Enum** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

What the file holds after the bytes a signature covers (3.35.0).

```csharp
public enum SignatureModifications
```

## Values

| Name | Description |
|---|---|
| `None` | Nothing: the signature covers the file to its end (trailing white space aside), so no change after signing is possible. |
| `SignaturesOnly` | Only later signatures and validation data were added after signing: new signature fields, their widgets and appearances, document timestamps and the `/DSS` dictionary. Viewers show such a signature as valid, with later signatures. |
| `Other` | The file was changed in another way after signing (page content, form values, annotations, metadata, a changed signed field), the signature's byte range leaves part of the signed revision unsigned, or the change could not be analysed. What a reader sees is not what was signed. |

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureModifications.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureModifications.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
