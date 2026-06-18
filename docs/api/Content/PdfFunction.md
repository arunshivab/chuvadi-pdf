# PdfFunction

**Class** in `Chuvadi.Pdf.Content` (Content)

A PDF function (PDF 32000-1:2008 §7.10): a mapping from an `InputCount`-dimensional input to an `OutputCount`-dimensional output. Use `Parse` to build one from a function dictionary, stream, or array, then `Evaluate` to apply it.

```csharp
public abstract class PdfFunction
```

## Properties

### `InputCount`

```csharp
int InputCount => _domain.Length / 2
```

The number of input values the function consumes (m).

### `OutputCount`

```csharp
abstract int OutputCount
```

The number of output values the function produces (n).

## Methods

### `DomainCopy`

```csharp
double[] DomainCopy() => (double[])_domain.Clone()
```

Returns a copy of this function's input domain as interval pairs (length 2·m).

**Returns:** A fresh array of domain bounds.

### `Evaluate`

```csharp
double[] Evaluate(double[] input)
```

Evaluates the function: clips `input` to the domain, applies the mapping, and clips the result to the range when one is defined.

**Parameters**

- `input` — The input values; length should equal `InputCount`.

**Returns:** The output values (length `OutputCount`).

### `Parse`

__static__

```csharp
static PdfFunction Parse(PdfPrimitive function, PdfObjectStore objects)
```

Parses a function object: a Type 2/3 dictionary, a Type 0/4 stream, or an array of n single-output functions (treated as one n-output function).

**Parameters**

- `function` — The function object or reference.
- `objects` — The object store used to resolve references and stream data.

**Returns:** The parsed function.

---

_Source: [`src/Chuvadi.Pdf.Content/PdfFunction.cs`](../../../src/Chuvadi.Pdf.Content/PdfFunction.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
