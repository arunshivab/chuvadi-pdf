# PdfNumberFormat

**Class** in `Chuvadi.Pdf.Documents` (Documents)

One step of a measurement unit chain (a `/NumberFormat` dictionary): its label and the factor converting the previous unit into it — the first format converts default user-space units into the first display unit.

```csharp
public sealed class PdfNumberFormat
```

## Properties

### `Unit`

```csharp
string Unit
```

Gets the unit label (`/U`), for example `"mm"` or `"ft"`.

### `ConversionFactor`

```csharp
double ConversionFactor
```

Gets the conversion factor (`/C`) from the previous unit into this one.

### `Fraction`

```csharp
PdfFractionFormat Fraction
```

Gets how fractional values are shown (`/F`).

### `Denominator`

```csharp
int Denominator
```

Gets the precision (`/D`): for decimals, a power of ten (100 = two places); for fractions, the denominator.

### `ForceDenominator`

```csharp
bool ForceDenominator
```

Gets whether fractions keep the full denominator (`/FD`) rather than being reduced.

### `ThousandsSeparator`

```csharp
string ThousandsSeparator
```

Gets the thousands separator (`/RT`).

### `DecimalSeparator`

```csharp
string DecimalSeparator
```

Gets the decimal separator (`/RD`).

### `PrefixSpacing`

```csharp
string PrefixSpacing
```

Gets the text placed before the label (`/PS`).

### `SuffixSpacing`

```csharp
string SuffixSpacing
```

Gets the text placed after the label (`/SS`).

### `LabelBefore`

```csharp
bool LabelBefore
```

Gets whether the label goes before the value (`/O /P`).

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfMeasure.cs`](../../../src/Chuvadi.Pdf.Documents/PdfMeasure.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
