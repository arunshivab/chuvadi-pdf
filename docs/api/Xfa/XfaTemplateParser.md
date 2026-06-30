# XfaTemplateParser

**Class** in `Chuvadi.Pdf.Xfa.Parse` (Xfa)

Parses the XFA `template` packet XML into a typed `XfaNode` tree. Unknown elements are skipped (their children are still visited), so the parser degrades gracefully on templates that use features beyond the current model.

```csharp
public static class XfaTemplateParser
```

## Methods

### `Parse`

__static__

```csharp
static XfaSubform? Parse(byte[] templateXml)
```

Parses template XML bytes into the root subform of the model tree.

**Parameters**

- `templateXml` — The raw template packet bytes (UTF-8).

**Returns:** The root `XfaSubform`, or null when no subform is found. <exception cref="ArgumentNullException">`templateXml` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Parse/XfaTemplateParser.cs`](../../../src/Chuvadi.Pdf.Xfa/Parse/XfaTemplateParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
