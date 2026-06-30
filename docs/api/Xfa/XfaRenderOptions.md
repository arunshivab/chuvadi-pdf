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

Gets the default options: no scripting, best-effort rendering.

### `FailOnUnsupported`

```csharp
bool FailOnUnsupported
```

Gets or sets a value indicating whether to throw on unsupported template constructs rather than skipping them. Defaults to `false`.

### `ScriptMode`

```csharp
XfaScriptMode ScriptMode
```

Gets or sets how embedded scripts are handled. Defaults to `XfaScriptMode.None`.

---

_Source: [`src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs`](../../../src/Chuvadi.Pdf.Xfa/Render/XfaRenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
