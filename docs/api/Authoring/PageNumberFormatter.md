# PageNumberFormatter

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Formats integers in the report numbering schemes.

```csharp
public static class PageNumberFormatter
```

## Methods

### `Format`

__static__

```csharp
static string Format(int value, NumberingFormat format)
```

Formats `value` (1-based) in the given scheme. Values below 1 format as Arabic digits in every scheme.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
