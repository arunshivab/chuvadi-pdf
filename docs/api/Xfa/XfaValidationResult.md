# XfaValidationResult

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

The result of running validate scripts: the nodes whose validation failed.

```csharp
public sealed class XfaValidationResult
```

## Properties

### `Failures`

```csharp
IReadOnlyList<XfaNode> Failures => _failures
```

Gets the nodes whose validate script returned a falsy result.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptRunner.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptRunner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
