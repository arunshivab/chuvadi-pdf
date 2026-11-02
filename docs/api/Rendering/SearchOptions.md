# SearchOptions

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Options controlling a search.

```csharp
public sealed class SearchOptions
```

## Properties

### `CaseSensitive`

```csharp
bool CaseSensitive
```

Match case-sensitively. Default false.

### `WholeWord`

```csharp
bool WholeWord
```

Require whole-word matches. Default false.

### `PageRangeStart`

```csharp
int? PageRangeStart
```

Optional inclusive start page (0-based). Default 0.

### `PageRangeEnd`

```csharp
int? PageRangeEnd
```

Optional exclusive end page (0-based). Default = page count.

### `MatchCompatibilityForms`

```csharp
bool MatchCompatibilityForms
```

Match Unicode compatibility forms as their plain characters (since 3.29; default true): the page text and the query are both compared in compatibility decomposition (NFKD), with the minus sign read as a hyphen-minus, so "H2O" finds "H₂O", "x2" finds "x²", "10-3" finds "10⁻³", "fi" finds the "ﬁ" ligature and "A" finds a full-width "Ａ". Match offsets and lengths stay in the page's own text. Set false to compare the characters exactly, as before 3.29.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
