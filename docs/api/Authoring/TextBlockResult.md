# TextBlockResult

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Result of a `PageBuilder.DrawTextBlock` call.

```csharp
public sealed class TextBlockResult
```

## Properties

### `HasOverflow`

```csharp
bool HasOverflow
```

True when the supplied bounds were too small to fit all the text.

### `RemainingText`

```csharp
string RemainingText
```

The portion of the text that didn't fit. Empty string if everything was drawn.

### `NextYFromTop`

```csharp
double NextYFromTop
```

The Y position (top-left coords) immediately below the last drawn line.

---

_Source: [`src/Chuvadi.Pdf.Authoring/Hyperlink.cs`](../../../src/Chuvadi.Pdf.Authoring/Hyperlink.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
