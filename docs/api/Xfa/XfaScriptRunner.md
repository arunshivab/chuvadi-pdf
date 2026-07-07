# XfaScriptRunner

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

Runs the scripts attached to a template's nodes for a given event. Each script executes in the context of its owning node. Any script that fails to parse or evaluate is skipped (fail-soft) so a single bad script never aborts rendering. Interactive events are never fired here — a static render has no event source — but their scripts remain attached for hosts that can drive them.

```csharp
public static class XfaScriptRunner
```

## Methods

### `RunInitialize`

__static__

```csharp
static void RunInitialize(XfaNode root, XfaScriptHost host)
```

Runs all `initialize` scripts across the tree, in document order. Each script's writes (typically `this.rawValue = ...`) mutate the model so later layout and rendering observe the computed values.

**Parameters**

- `root` — The template root.
- `host` — The scripting host bound to the same root. <exception cref="ArgumentNullException">A required argument is null.</exception>

### `RunCalculate`

__static__

```csharp
static void RunCalculate(XfaNode root, XfaScriptHost host)
```

Runs all `calculate` scripts across the tree, in document order.

**Parameters**

- `root` — The template root.
- `host` — The scripting host bound to the same root. <exception cref="ArgumentNullException">A required argument is null.</exception>

### `RunValidate`

__static__

```csharp
static XfaValidationResult RunValidate(XfaNode root, XfaScriptHost host)
```

Runs all `validate` scripts across the tree. A script whose result coerces to false marks its owning node as a validation failure.

**Parameters**

- `root` — The template root.
- `host` — The scripting host bound to the same root.

**Returns:** The validation result. <exception cref="ArgumentNullException">A required argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptRunner.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptRunner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
