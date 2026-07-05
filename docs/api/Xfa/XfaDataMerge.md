# XfaDataMerge

**Class** in `Chuvadi.Pdf.Xfa.Parse` (Xfa)

Merges values from the datasets packet into a parsed template tree by resolving each field's `XfaField.DataRef` against the document's extracted `XfaDataField` list.

```csharp
public static class XfaDataMerge
```

## Methods

### `Apply`

__static__

```csharp
static void Apply(XfaNode root, IReadOnlyList<XfaDataField> dataFields)
```

Fills field values in `root` from `dataFields`. A field whose `XfaField.DataRef` resolves to a data value has its `XfaField.Value` text replaced by the merged value.

**Parameters**

- `root` — The parsed template root to populate in place.
- `dataFields` — The datasets fields extracted from the document. <exception cref="ArgumentNullException">A required argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Parse/XfaDataMerge.cs`](../../../src/Chuvadi.Pdf.Xfa/Parse/XfaDataMerge.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
