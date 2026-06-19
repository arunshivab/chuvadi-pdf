# PatternRule

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

A regex pattern that locates text to redact, with optional per-page filtering.

```csharp
public sealed class PatternRule
```

## Remarks

Use this when the exact rectangles aren't known up front (e.g., "redact every SSN", "redact every email address"). At redaction time the document's text is extracted with glyph positions; any match is resolved back to a device-space rectangle and fed into the existing rectangle-based redactor.

## Constructors

### `PatternRule(string pattern, int[]? pageIndices = null, Func<string, bool>? validator = null)`

Initialises a new `PatternRule`.

**Parameters**

- `pattern` — The regex pattern. Must compile against the .NET regex flavour. Matching is case-sensitive by default; pass an already-compiled `Regex` via the other constructor to override.
- `pageIndices` — Optional list of zero-based page indices to restrict the rule to. When null, applies to all pages.
- `validator` — Optional post-match predicate, run on each regex match's text. When it returns `false` the match is not redacted. Use this to reject false positives via a checksum (e.g. `PatternValidators`).

### `PatternRule(Regex regex, int[]? pageIndices = null, Func<string, bool>? validator = null)`

Initialises a new `PatternRule` from a pre-compiled regex.

## Properties

### `Regex`

```csharp
Regex Regex
```

Gets the compiled regex.

### `PageIndices`

```csharp
int[]? PageIndices
```

Gets the page indices this rule applies to, or null for all pages.

### `Validator`

```csharp
Func<string, bool>? Validator
```

Gets the optional post-match validator, or `null` to redact every regex match unconditionally.

## Methods

### `AppliesToPage`

```csharp
bool AppliesToPage(int pageIndex)
```

Returns true if this rule applies to the given zero-based page index.

---

_Source: [`src/Chuvadi.Pdf.Redaction/PatternRule.cs`](../../../src/Chuvadi.Pdf.Redaction/PatternRule.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
