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

## Methods

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

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptHost.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptHost.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
