# XfaScriptMode

**Enum** in `Chuvadi.Pdf.Xfa.Render` (Xfa)

How the renderer treats embedded XFA scripts.

```csharp
public enum XfaScriptMode
```

## Values

| Name | Description |
|---|---|
| `None` | Do not execute scripts; render the merged/last-saved values. |
| `CalculationsOnly` | Execute only calculation scripts, not validations or events. |
| `Full` | Execute calculation, validation, and initialization scripts. |

---

_Source: [`src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs`](../../../src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
