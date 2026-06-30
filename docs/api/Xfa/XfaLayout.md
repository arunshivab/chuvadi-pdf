# XfaLayout

**Enum** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

The layout strategy a container applies to its children.

```csharp
public enum XfaLayout
```

## Values

| Name | Description |
|---|---|
| `Position` | Children are positioned by their explicit x/y coordinates. |
| `TopToBottom` | Children flow top to bottom. |
| `LeftRightTopToBottom` | Children flow left to right, wrapping to new rows top to bottom. |
| `Table` | Children are laid out as table rows. |
| `Row` | The container is a single table row of cells. |
| `Tb` | Children flow into a single tabbed line. |

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
