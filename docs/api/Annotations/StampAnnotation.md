# StampAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Rubber-stamp annotation (§12.5.6.12), e.g., "Approved", "Confidential".

```csharp
public sealed class StampAnnotation : PdfAnnotation
```

## Properties

### `StampName`

```csharp
string StampName
```

Gets the stamp icon name. Standard values include Approved, Experimental, NotApproved, AsIs, Expired, NotForPublicRelease, Confidential, Final, Sold, Departmental, ForComment, TopSecret, Draft, ForPublicRelease.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
