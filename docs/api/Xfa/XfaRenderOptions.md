# XfaRenderOptions

**Class** in `Chuvadi.Pdf.Xfa.Render` (Xfa)

Options controlling XFA rendering.

```csharp
public sealed class XfaRenderOptions
```

## Properties

### `Default`

__static__

```csharp
static XfaRenderOptions Default
```

Gets the default options: full scripting, best-effort rendering.

### `FailOnUnsupported`

```csharp
bool FailOnUnsupported
```

Gets or sets a value indicating whether to throw on unsupported template constructs rather than skipping them. Defaults to `false`.

### `ScriptMode`

```csharp
XfaScriptMode ScriptMode
```

Gets or sets how embedded scripts are handled. Defaults to `XfaScriptMode.None` (3.36.0): embedded initialize, calculate and validate scripts are not run unless `XfaScriptMode.Full` is set. When scripts run, a per-render budget (one million script steps, a one-million-character string cap, and the operation's cancellation token) bounds them; exceeding the budget raises an `XfaScriptException`, the remaining scripts are skipped, and the form renders with the values computed so far.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs`](../../../src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
