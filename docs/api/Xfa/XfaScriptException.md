# XfaScriptException

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

Thrown when a script cannot be parsed or evaluated. The script runner catches this and fails soft, leaving form state untouched.

```csharp
public sealed class XfaScriptException : Exception
```

## Constructors

### `XfaScriptException()`

Initializes a new instance of the `XfaScriptException` class.

### `XfaScriptException(string message)`

Initializes a new instance of the `XfaScriptException` class.

**Parameters**

- `message` — The error message.

### `XfaScriptException(string message, Exception innerException)`

Initializes a new instance of the `XfaScriptException` class.

**Parameters**

- `message` — The error message.
- `innerException` — The inner exception.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptException.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
