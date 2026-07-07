# XfaScript

**Class** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A script attached to a node through an XFA event. Captures the scripting language, the triggering event, and the raw source text.

```csharp
public sealed class XfaScript
```

## Constructors

### `XfaScript(XfaScriptLanguage language, XfaScriptEvent @event, string source)`

Initializes a new instance of the `XfaScript` class.

**Parameters**

- `language` — The scripting language.
- `event` — The triggering event.
- `source` — The raw script source text.

## Properties

### `Language`

```csharp
XfaScriptLanguage Language
```

Gets the scripting language.

### `Event`

```csharp
XfaScriptEvent Event
```

Gets the event that triggers this script.

### `Source`

```csharp
string Source
```

Gets the raw script source text.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaScript.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaScript.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
