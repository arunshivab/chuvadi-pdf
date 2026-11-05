# FontEmbeddingPermission

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The embedding permission of a font program (OpenType `OS/2` `fsType`).

```csharp
public enum FontEmbeddingPermission
```

## Values

| Name | Description |
|---|---|
| `Unknown` | Not known: the program has no `OS/2` table (Type 1, bare CFF) or is not embedded. |
| `Installable` | Installable embedding (`fsType` 0): no restriction. |
| `Restricted` | Restricted licence embedding (bit 1): must not be embedded or sent to a device that keeps it. |
| `PreviewAndPrint` | Preview and print embedding (bit 2): may be embedded for viewing and printing. |
| `Editable` | Editable embedding (bit 3): may be embedded for viewing, printing and editing. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
