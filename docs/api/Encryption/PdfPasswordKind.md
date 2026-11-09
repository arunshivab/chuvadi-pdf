# PdfPasswordKind

**Enum** in `Chuvadi.Pdf.Encryption` (Encryption)

Which password opened an encrypted document (3.32.0).

```csharp
public enum PdfPasswordKind
```

## Remarks

A password is tested as the owner password first and then as the user password, so a password that is both reports `Owner`.

## Values

| Name | Description |
|---|---|
| `NoPassword` | The empty user password opened it: the document opens without asking. |
| `User` | A non-empty user password opened it. |
| `Owner` | The owner password opened it. |

---

_Source: [`src/Chuvadi.Pdf.Encryption/PdfPasswordKind.cs`](../../../src/Chuvadi.Pdf.Encryption/PdfPasswordKind.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
