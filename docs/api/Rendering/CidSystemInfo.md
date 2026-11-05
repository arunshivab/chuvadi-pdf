# CidSystemInfo

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The `/CIDSystemInfo` of a CIDFont (§9.7.3).

```csharp
public sealed record CidSystemInfo(string Registry, string Ordering, int Supplement)
```

## Parameters

- `Registry` — The issuer of the character collection, for example "Adobe".
- `Ordering` — The character collection, for example "Identity" or "Japan1".
- `Supplement` — The supplement number.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
