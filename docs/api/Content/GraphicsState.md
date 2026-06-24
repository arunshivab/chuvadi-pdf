# GraphicsState

**Class** in `Chuvadi.Pdf.Content` (Content)

Represents the graphics and text state at a point in content stream processing.

```csharp
public sealed class GraphicsState
```

## Remarks

The graphics state is maintained as a stack. The operators q/Q push/pop the state. This class represents one level on that stack. For Phase 1 text extraction, only text-relevant state is tracked: font, font size, text matrix, text line matrix, character spacing, word spacing, and horizontal scaling. PDF 32000-1:2008 §8.4 — Graphics state. PDF 32000-1:2008 §9.3 — Text state parameters, Table 104.

## Constructors

### `GraphicsState()`

Creates a new `GraphicsState` with default values.

## Properties

### `Font`

```csharp
PdfFont Font
```

Gets or sets the current font. PDF 32000-1:2008 §9.3.1 — Tf operator.

### `FontSize`

```csharp
double FontSize
```

Gets or sets the current font size in text space units.

### `CharacterSpacing`

```csharp
double CharacterSpacing
```

Gets or sets the character spacing (Tc). Added to the horizontal or vertical displacement after each glyph. PDF 32000-1:2008 §9.3.2.

### `WordSpacing`

```csharp
double WordSpacing
```

Gets or sets the word spacing (Tw). Added to displacement after space character (code 0x20). PDF 32000-1:2008 §9.3.3.

### `HorizontalScaling`

```csharp
double HorizontalScaling
```

Gets or sets the horizontal scaling (Tz) as a percentage (default 100). PDF 32000-1:2008 §9.3.4.

### `TextLeading`

```csharp
double TextLeading
```

Gets or sets the text leading (TL) — vertical distance between lines. PDF 32000-1:2008 §9.3.5.

### `TextRise`

```csharp
double TextRise
```

Gets or sets the text rise (Ts) — vertical displacement from baseline. PDF 32000-1:2008 §9.3.6.

### `TextMatrix`

```csharp
Matrix3x3 TextMatrix
```

Gets or sets the text matrix (Tm) — transforms text space to user space. Updated by Tm, Td, TD, T*, and text-showing operators. PDF 32000-1:2008 §9.4.1.

### `TextLineMatrix`

```csharp
Matrix3x3 TextLineMatrix
```

Gets or sets the text line matrix — tracks the start of the current text line. Updated by Td, TD, and T*. PDF 32000-1:2008 §9.4.1.

### `CurrentTransformationMatrix`

```csharp
Matrix3x3 CurrentTransformationMatrix
```

Gets or sets the current transformation matrix (CTM). Updated by the cm operator. PDF 32000-1:2008 §8.4.4.

## Methods

### `Clone`

```csharp
GraphicsState Clone()
```

Creates a deep copy of this state for the graphics state stack.

---

_Source: [`src/Chuvadi.Pdf.Content/GraphicsState.cs`](../../../src/Chuvadi.Pdf.Content/GraphicsState.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
