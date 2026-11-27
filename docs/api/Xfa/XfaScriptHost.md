# XfaScriptHost

**Class** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

The host surface shared by the FormCalc and JavaScript engines. Resolves SOM references (dotted node paths such as `Certificate.CompanyName` or `data.Certificate.City`) against the template tree and reads or writes node properties (`rawValue`, `value`, `presence`).

```csharp
public sealed class XfaScriptHost
```

## Constructors

### `XfaScriptHost(XfaNode root)`

Initializes a new instance of the `XfaScriptHost` class.

**Parameters**

- `root` — The template root the scripts resolve references against. <exception cref="ArgumentNullException">`root` is null.</exception>

### `XfaScriptHost(XfaNode root, CancellationToken cancellationToken)`

Initializes a new instance of the `XfaScriptHost` class that honours `cancellationToken` while scripts run (3.36.0).

**Parameters**

- `root` — The template root the scripts resolve references against.
- `cancellationToken` — The token checked as scripts run. <exception cref="ArgumentNullException">`root` is null.</exception>

## Methods

### `ChargeStep`

```csharp
void ChargeStep()
```

Charges one script step against the per-render budget and honours the cancellation token (3.36.0). The engines call it as they run. Throws `XfaScriptException` when `MaxScriptSteps` is passed — the runner then skips the rest of the scripts — or `OperationCanceledException` when the token is cancelled, which ends the render. <exception cref="XfaScriptException">The step budget is spent.</exception>

### `CheckStringLength`

__static__

```csharp
static void CheckStringLength(long length)
```

Guards against runaway string growth (3.36.0): throws `XfaScriptException` when a string a script would build is longer than `MaxStringLength`.

**Parameters**

- `length` — The length of the string about to be built. <exception cref="XfaScriptException">The length cap is passed.</exception>

### `Resolve`

```csharp
XfaNode? Resolve(string reference, XfaNode? context)
```

Resolves a SOM reference to a node, or null when it cannot be resolved. Supports dotted paths, an optional leading `data.` / `xfa.` / `$record.` root, and a bare leaf name.

**Parameters**

- `reference` — The SOM reference expression.
- `context` — The node bound to `this`, for relative refs.

**Returns:** The resolved node, or null.

### `GetProperty`

__static__

```csharp
static string GetProperty(XfaNode node, string property)
```

Reads a property of a node as a string.

**Parameters**

- `node` — The node to read.
- `property` — The property name (rawValue / value / text / presence / name).

**Returns:** The property value, or the empty string when unset.

### `SetProperty`

__static__

```csharp
static void SetProperty(XfaNode node, string property, string value)
```

Writes a property of a node.

**Parameters**

- `node` — The node to modify.
- `property` — The property name (rawValue / value / text / presence).
- `value` — The new value.

## Fields

### `MaxScriptSteps`

```csharp
const long MaxScriptSteps = 1_000_000
```

One million — the script-step budget for one render (3.36.0).

### `MaxStringLength`

```csharp
const int MaxStringLength = 1_000_000
```

One million — the cap on a script string's length (3.36.0).

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptHost.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptHost.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
