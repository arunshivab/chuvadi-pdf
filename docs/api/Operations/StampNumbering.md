# StampNumbering

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Describes a running numbering sequence for the `{number}` stamp token: a free prefix and suffix, a start value, an optional zero-pad width, a `NumberingFormat` style, and first-page handling. Used with the `TextStamper` numbering overload to produce Bates-style labels such as `BATES-000123` in a single stamp pass.

```csharp
public sealed class StampNumbering
```

## Properties

### `Prefix`

```csharp
string Prefix
```

Gets or initialises the text placed before the number. Default: empty.

### `Suffix`

```csharp
string Suffix
```

Gets or initialises the text placed after the number. Default: empty.

### `StartValue`

```csharp
int StartValue
```

Gets or initialises the value assigned to the first counted page. Default: 1.

### `PadWidth`

```csharp
int PadWidth
```

Gets or initialises the minimum width of the numeric core, left-filled with zeros. Applies to `NumberingFormat.Arabic` only; ignored for roman and letter styles. Zero (the default) means no padding.

### `Numbering`

```csharp
NumberingFormat Numbering
```

Gets or initialises the numbering style. Default: `NumberingFormat.Arabic`.

### `FirstPage`

```csharp
StampFirstPageMode FirstPage
```

Gets or initialises first-page handling. Default: `StampFirstPageMode.Number`.

## Methods

### `ResolveValue`

```csharp
int? ResolveValue(int pageIndex)
```

Returns the sequence value for the given zero-based document page index, honouring `FirstPage`, or null when the page is not counted (and therefore not stamped). The counter is anchored to the literal first page (index 0) regardless of which pages are selected for stamping.

**Parameters**

- `pageIndex` — The zero-based document page index.

**Returns:** The sequence value, or null when the page is skipped.

### `Format`

```csharp
string Format(int value)
```

Formats a sequence value into its styled label: the prefix, the formatted (and, for `NumberingFormat.Arabic`, zero-padded) number, then the suffix.

**Parameters**

- `value` — The sequence value to format.

**Returns:** The styled label.

---

_Source: [`src/Chuvadi.Pdf.Operations/StampNumbering.cs`](../../../src/Chuvadi.Pdf.Operations/StampNumbering.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
