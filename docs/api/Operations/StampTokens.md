# StampTokens

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Resolves stamp templates against a `StampContext`.

```csharp
public static class StampTokens
```

## Methods

### `Resolve`

__static__

```csharp
static string Resolve(string template, StampContext context)
```

Expands all tokens in `template` for the given context.

**Parameters**

- `template` — The template text containing tokens.
- `context` — The per-page values.

**Returns:** The fully substituted text. <exception cref="ArgumentNullException"> Thrown when `template` or `context` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/StampTokens.cs`](../../../src/Chuvadi.Pdf.Operations/StampTokens.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
