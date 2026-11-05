# FontEmbeddingRights

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A font program's embedding rights (OpenType `OS/2` `fsType`).

```csharp
public readonly record struct FontEmbeddingRights(FontEmbeddingPermission Permission, bool NoSubsetting, bool BitmapOnly)
```

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
