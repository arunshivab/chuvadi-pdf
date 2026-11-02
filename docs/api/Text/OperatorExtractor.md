# OperatorExtractor

**Class** in `Chuvadi.Pdf.Text` (Text)

Extracts text from a list of `TextFragment` objects in content stream order with simple heuristics for word and line breaks.

```csharp
public sealed class OperatorExtractor
```

## Remarks

This is the fastest extraction strategy. It preserves the order in which text operators appear in the content stream, which matches reading order for most well-structured born-digital PDFs. Heuristics applied: 
 
-  A gap between two fragments whose X distance exceeds half the font size is treated as a word space.  
-  A vertical drop of more than half the font size between two fragments is treated as a line break.   Raised and lowered text is written as `Superscripts` says (since 3.29). For complex layouts (multi-column, tables) use `LayoutExtractor`. PDF 32000-1:2008 §9.10 — Extraction of text content.

## Properties

### `Superscripts`

```csharp
SuperscriptMode Superscripts
```

Gets or initialises how raised and lowered text is written (since 3.29). Default: `SuperscriptMode.Unicode`.

## Methods

### `Extract`

```csharp
string Extract(List<TextFragment> fragments)
```

Converts a list of text fragments to a plain text string using stream-order heuristics.

**Parameters**

- `fragments` — Fragments from `ContentStreamParser`.

**Returns:** The extracted text with word spaces and line breaks inserted.

---

_Source: [`src/Chuvadi.Pdf.Text/OperatorExtractor.cs`](../../../src/Chuvadi.Pdf.Text/OperatorExtractor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
