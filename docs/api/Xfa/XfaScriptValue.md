# XfaScriptValue

**Struct** in `Chuvadi.Pdf.Xfa.Scripting` (Xfa)

A dynamic script value: a string, a number, a boolean, a node reference, or null/undefined. Shared by the FormCalc and JavaScript engines. Coercions follow the pragmatic rules XFA form scripts rely on.

```csharp
public readonly struct XfaScriptValue : IEquatable<XfaScriptValue>
```

## Properties

### `Undefined`

__static__

```csharp
static XfaScriptValue Undefined
```

Gets the undefined/null value.

### `IsUndefined`

```csharp
bool IsUndefined => Kind == ValueKind.Undefined
```

Gets a value indicating whether this value is undefined.

### `IsNode`

```csharp
bool IsNode => Kind == ValueKind.Node
```

Gets a value indicating whether this value is a node reference.

### `IsString`

```csharp
bool IsString => Kind == ValueKind.String
```

Gets a value indicating whether this value is a string.

## Methods

### `AsNode`

```csharp
XfaNode? AsNode() => _node
```

Gets the referenced node, or null when this is not a node value.

**Returns:** The node or null.

### `Equals`

```csharp
bool Equals(XfaScriptValue other)
```

<inheritdoc />

### `Equals`

```csharp
override bool Equals(object? obj) => obj is XfaScriptValue other && Equals(other)
```

<inheritdoc />

### `==`

__static__

```csharp
static bool operator ==(XfaScriptValue left, XfaScriptValue right) => left.Equals(right)
```

Compares two values for equality.

**Parameters**

- `left` — The left value.
- `right` — The right value.

**Returns:** True when equal.

### `!=`

__static__

```csharp
static bool operator !=(XfaScriptValue left, XfaScriptValue right) => !left.Equals(right)
```

Compares two values for inequality.

**Parameters**

- `left` — The left value.
- `right` — The right value.

**Returns:** True when not equal.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptValue.cs`](../../../src/Chuvadi.Pdf.Xfa/Scripting/XfaScriptValue.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
