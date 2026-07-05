# XfaPageSetRelation

**Enum** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

How a page set generates pages from its child page areas.

```csharp
public enum XfaPageSetRelation
```

## Values

| Name | Description |
|---|---|
| `OrderedOccurrence` | Walk the child page areas in document order, honoring each one's occurrence counts; unbounded page areas repeat for overflow. |
| `DuplexPaginated` | Generate front/back page pairs for double-sided output. |
| `SimplexPaginated` | Generate single-sided pages. |

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
