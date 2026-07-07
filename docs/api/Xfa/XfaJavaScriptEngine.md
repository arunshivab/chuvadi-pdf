# XfaJavaScriptEngine

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

A small JavaScript interpreter covering the language subset that XFA form scripts use. Evaluates a script in the context of a `this` node against a `XfaScriptHost`.

```csharp
public sealed class XfaJavaScriptEngine
```

## Constructors

### `XfaJavaScriptEngine(XfaScriptHost host)`

Initializes a new instance of the `XfaJavaScriptEngine` class.

**Parameters**

- `host` — The scripting host for SOM resolution. <exception cref="ArgumentNullException">`host` is null.</exception>

## Methods

### `Execute`

```csharp
void Execute(string source, XfaNode? thisNode)
```

Executes JavaScript source in the context of a node.

**Parameters**

- `source` — The script source.
- `thisNode` — The node bound to `this`. <exception cref="ArgumentNullException">`source` is null.</exception> <exception cref="XfaScriptException">The script uses an unsupported construct.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaJavaScriptEngine.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaJavaScriptEngine.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
