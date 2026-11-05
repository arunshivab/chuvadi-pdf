# PdfPermissions

**Enum** in `Chuvadi.Pdf.Primitives` (Primitives)

Permission bits carried in a PDF document's encryption dictionary, controlling which operations a user-password holder is allowed to perform. PDF 32000-1:2008 §7.6.3.2 — User access permissions.

```csharp
public enum PdfPermissions
```

## Remarks

Permissions are advisory and enforced cooperatively by conforming readers; a sufficiently determined attacker who already has the user password can ignore them. The flags exist to let well-behaved software honour the document author's intent. The owner password bypasses all permission checks. Documents opened with the owner password should not surface `PdfPermissionException` for any operation. Bit values match the PDF specification's P-entry semantics: a set bit means the corresponding action is <em>allowed</em>, a cleared bit means it is <em>denied</em>.

## Values

| Name | Description |
|---|---|
| `None` | No permissions granted. All restricted operations denied. |
| `Print` | Permission to print the document (low-resolution if `PrintHighQuality` is not also set). |
| `ModifyContents` | Permission to modify the document's contents other than by adding annotations or filling form fields. |
| `CopyContents` | Permission to copy or otherwise extract text and graphics from the document. Required for text selection and clipboard copy. |
| `ModifyAnnotations` | Permission to add, modify, or delete annotations and form fields (including signature fields). |
| `FillForms` | Permission to fill existing interactive form fields, including signature fields, without altering the form's structure. |
| `ExtractAccessibility` | Permission to extract text and graphics for accessibility purposes (screen readers, content reflow). |
| `Assemble` | Permission to assemble the document (insert, rotate, or delete pages and create bookmarks or thumbnails), even when `ModifyContents` is denied. |
| `PrintHighQuality` | Permission to print the document at high resolution. Without this flag but with `Print`, the document may be printed only at a degraded resolution. |
| `All` | All permissions granted. Used as the default when authoring a new encrypted document without explicit restrictions. |

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPermissions.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPermissions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
