# FontProgramKind

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The kind of an embedded font program (§9.9, Table 124).

```csharp
public enum FontProgramKind
```

## Values

| Name | Description |
|---|---|
| `None` | The font is not embedded (or is a Type 3 font, which has no program). |
| `Type1` | A Type 1 program (`/FontFile`). |
| `TrueType` | A TrueType program (`/FontFile2`). |
| `Cff` | A bare CFF program for a simple font (`/FontFile3`, `/Subtype /Type1C`). |
| `CidCff` | A bare CID-keyed CFF program (`/FontFile3`, `/Subtype /CIDFontType0C`). |
| `OpenType` | An OpenType program (`/FontFile3`, `/Subtype /OpenType`). |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
