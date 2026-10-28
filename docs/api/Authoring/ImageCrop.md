# ImageCrop

**Record** in `Chuvadi.Pdf.Authoring` (Authoring)

The part of an image to show, in source pixels measured from the image's top-left corner. The rectangle may extend past the image (the area outside shows nothing), as cropping in Word and Excel allows.

```csharp
public readonly record struct ImageCrop(double X, double Y, double Width, double Height)
```

---

_Source: [`src/Chuvadi.Pdf.Authoring/ImageCrop.cs`](../../../src/Chuvadi.Pdf.Authoring/ImageCrop.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
