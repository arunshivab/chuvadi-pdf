# XfaScriptEvent

**Enum** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

The event that triggers a script (the `activity` attribute).

```csharp
public enum XfaScriptEvent
```

## Values

| Name | Description |
|---|---|
| `Initialize` | Runs once when the form initializes. |
| `Calculate` | Recomputes a field value when dependencies change. |
| `Validate` | Validates a field value. |
| `PreSign` | Runs before a signature is applied. |
| `PostSign` | Runs after a signature is applied. |
| `Interactive` | An interactive event with no source in static rendering. |

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaEnums.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
