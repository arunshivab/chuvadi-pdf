# XfaPresence

**Enum** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

Whether and how a node participates in layout and rendering.

```csharp
public enum XfaPresence
```

## Values

| Name | Description |
|---|---|
| `Visible` | The node is laid out and rendered normally. |
| `Invisible` | The node is not rendered but still occupies layout space. |
| `Hidden` | The node is neither rendered nor allotted layout space. |
| `Inactive` | The node is excluded from layout but kept in the form model. |

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
