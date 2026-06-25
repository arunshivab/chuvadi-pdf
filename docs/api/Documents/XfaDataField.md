# XfaDataField

**Class** in `Chuvadi.Pdf.Documents` (Documents)

A single value drawn from the XFA `datasets` packet's data layer (`&lt;xfa:data&gt;`): the data element's path, its text value, and a best-effort widget geometry the host can use to overlay the value onto the rendered template. XFA 3.3 §A.2.

```csharp
public sealed class XfaDataField
```

## Properties

### `NodePath`

```csharp
string NodePath
```

Gets the dotted element path beneath `&lt;xfa:data&gt;`, built from the data elements' local names — for example `"data.ZMCA_NCA_INC29_STRUCT.CIN"`.

### `Value`

```csharp
string? Value
```

Gets the leaf element's text value. Empty string for an element present but empty (for example `&lt;CIN/&gt;`); null is not produced by the datasets walker but the type permits it for callers that synthesise fields.

### `Geometry`

```csharp
XfaGeometry? Geometry
```

Gets best-effort widget geometry for overlaying the value, or null when no AcroForm widget matched this field's name. See `XfaGeometry`.

---

_Source: [`src/Chuvadi.Pdf.Documents/XfaDataField.cs`](../../../src/Chuvadi.Pdf.Documents/XfaDataField.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
