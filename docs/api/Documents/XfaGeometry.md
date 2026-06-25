# XfaGeometry

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Best-effort geometry for an `XfaDataField`, taken from a matching AcroForm widget annotation's `/Rect`. Geometry is only available for fields whose value is mirrored by a traditional AcroForm widget (typical of hybrid XFA); for static or dynamic XFA whose layout is produced by an XFA processor, no widget exists and `XfaDataField.Geometry` is null. PDF 32000-1:2008 §12.5.2.

```csharp
public sealed class XfaGeometry
```

## Properties

### `PageIndex`

```csharp
int PageIndex
```

Gets the zero-based index of the page carrying the matched widget, or `-1` when the page could not be determined from the page `/Annots` arrays.

### `Rectangle`

```csharp
PdfRectangle Rectangle
```

Gets the widget rectangle in PDF user space (the AcroForm widget's `/Rect`), suitable for overlaying the field's value onto the rendered page. PDF 32000-1:2008 §12.5.2.

---

_Source: [`src/Chuvadi.Pdf.Documents/XfaGeometry.cs`](../../../src/Chuvadi.Pdf.Documents/XfaGeometry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
