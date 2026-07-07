# XfaFormCalcEngine

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

A FormCalc interpreter covering the language features XFA form scripts use. Evaluates a script in the context of a `this` node against a `XfaScriptHost`.

```csharp
public sealed class XfaFormCalcEngine
```

## Constructors

### `XfaFormCalcEngine(XfaScriptHost host)`

Initializes a new instance of the `XfaFormCalcEngine` class.

**Parameters**

- `host` — The scripting host for SOM resolution. <exception cref="ArgumentNullException">`host` is null.</exception>

## Methods

### `Execute`

```csharp
string Execute(string source, XfaNode? thisNode)
```

Executes FormCalc source in the context of a node.

**Parameters**

- `source` — The script source.
- `thisNode` — The node bound to `this`.

**Returns:** The value of the final expression, coerced to a string. <exception cref="ArgumentNullException">`source` is null.</exception> <exception cref="XfaScriptException">The script uses an unsupported construct.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaFormCalcEngine.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaFormCalcEngine.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
