# ScriptClassifier

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Classifies code points by script and splits text into script runs.

```csharp
public static class ScriptClassifier
```

## Methods

### `Classify`

__static__

```csharp
static LipiScript Classify(int codepoint)
```

Returns the LiPi script for a Unicode code point.

**Parameters**

- `codepoint` — The Unicode scalar value.

**Returns:** The matching script, or `LipiScript.Latin` when outside the Indic blocks.

### `Split`

__static__

```csharp
static IReadOnlyList<ScriptRun> Split(string text)
```

Splits text into maximal same-script runs. Whitespace attaches to the current run so that interleaved spaces do not fragment a passage.

**Parameters**

- `text` — The text to split.

**Returns:** The ordered script runs. <exception cref="ArgumentNullException">`text` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Authoring/LipiScript.cs`](../../../src/Chuvadi.Pdf.Authoring/LipiScript.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
