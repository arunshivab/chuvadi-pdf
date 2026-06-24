# ContentStreamParser

**Class** in `Chuvadi.Pdf.Content` (Content)

Parses a PDF content stream and extracts text fragments with their approximate positions.

```csharp
public sealed class ContentStreamParser
```

## Remarks

A PDF content stream is a sequence of operands followed by an operator keyword. This parser processes the operators relevant to text extraction: Text object operators: BT (begin text), ET (end text). Text state operators: Tf (set font/size), Tc, Tw, Tz, TL, Tr, Ts. Text positioning: Td, TD, Tm, T*. Text showing: Tj, TJ, ' (apostrophe), " (quote). Graphics state: q (save), Q (restore), cm (concat matrix). All other operators are parsed for their operands (so the operand stack stays clean) but their effects are ignored. PDF 32000-1:2008 §9.4 — Text objects and operators.

## Constructors

### `ContentStreamParser(IPdfObjectResolver resolver, PdfDictionary? resources)`

Initialises a `ContentStreamParser` for a page.

**Parameters**

- `resolver` — Resolves indirect object references.
- `resources` — The page Resources dictionary, or null.

## Methods

### `Parse`

```csharp
List<TextFragment> Parse(byte[] streams)
```

Parses one or more content streams and returns extracted text fragments.

**Parameters**

- `streams` — The decoded content stream bytes. For pages with multiple content streams, concatenate them with a single space separator before calling.

**Returns:** A list of `TextFragment` objects in stream order.

---

_Source: [`src/Chuvadi.Pdf.Content/ContentStreamParser.cs`](../../../src/Chuvadi.Pdf.Content/ContentStreamParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
