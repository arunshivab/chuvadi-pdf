# PdfMeasurement

**Struct** in `Chuvadi.Pdf.Documents` (Documents)

A measured value in a drawing's real-world units.

```csharp
public readonly struct PdfMeasurement : IEquatable<PdfMeasurement>
```

## Properties

### `Value`

```csharp
double Value
```

Gets the value in the first (largest) unit of the chain.

### `Unit`

```csharp
string Unit
```

Gets the first unit's label.

### `Formatted`

```csharp
string Formatted
```

Gets the value formatted as the document asks (precision, fractions, separators, multi-unit chains such as `5 ft 3 in`).

## Methods

### `Equals`

```csharp
override bool Equals(object? obj) => obj is PdfMeasurement other && Equals(other)
```

<inheritdoc />

### `GetHashCode`

```csharp
override int GetHashCode() => HashCode.Combine(Value, Unit, Formatted)
```

<inheritdoc />

### `ToString`

```csharp
override string ToString() => Formatted
```

<inheritdoc />

### `==`

__static__

```csharp
static bool operator ==(PdfMeasurement left, PdfMeasurement right) => left.Equals(right)
```

Compares two measurements for equality.

**Parameters**

- `left` — The first measurement.
- `right` — The second measurement.

**Returns:** Whether they are equal.

### `!=`

__static__

```csharp
static bool operator !=(PdfMeasurement left, PdfMeasurement right) => !left.Equals(right)
```

Compares two measurements for inequality.

**Parameters**

- `left` — The first measurement.
- `right` — The second measurement.

**Returns:** Whether they differ.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfMeasure.cs`](../../../src/Chuvadi.Pdf.Documents/PdfMeasure.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
