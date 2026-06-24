# StampContext

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Substitutes tokens in a stamp template into final text for one page. Supported tokens: 
 
- `{page}` — 1-based page number (arabic). 
- `{page:roman}` / `{page:ROMAN}` — lower/upper roman numerals. 
- `{page:alpha}` / `{page:ALPHA}` — lower/upper bijective base-26 (a, b, … z, aa, …). 
- `{total}` — total page count. 
- `{filename}` — source file name without directory path. 
- `{filepath}` — full source file path as supplied. 
- `{number}` — styled running number (Bates) supplied via the `TextStamper` numbering overload; empty when none is supplied. 
- `{date:FORMAT}`, `{time:FORMAT}`, `{datetime:FORMAT}` — the caller-supplied timestamp formatted with a .NET format string.  A literal brace is written as `{{` or `}}`. Unknown tokens are left verbatim. Date/time tokens render empty when no timestamp is supplied.

```csharp
public sealed class StampContext
```

## Properties

### `PageNumber`

```csharp
int PageNumber
```

Gets the 1-based page number.

### `TotalPages`

```csharp
int TotalPages
```

Gets the total page count.

### `FilePath`

```csharp
string? FilePath
```

Gets the source file path, or null.

### `Timestamp`

```csharp
DateTimeOffset? Timestamp
```

Gets the caller-supplied timestamp, or null.

### `Number`

```csharp
string? Number
```

Gets the pre-formatted numbering label for the `{number}` token, or null.

---

_Source: [`src/Chuvadi.Pdf.Operations/StampTokens.cs`](../../../src/Chuvadi.Pdf.Operations/StampTokens.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
