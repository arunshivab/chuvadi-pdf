# XfaMeasurement

**Struct** in `Chuvadi.Pdf.Xfa.Model` (Xfa)

A linear measurement parsed from an XFA template attribute (for example "12.7mm", "0.5in", "36pt"). Stored internally in PDF points (1/72 inch), which is the unit used by the authoring and rendering layers.

```csharp
public readonly struct XfaMeasurement : IEquatable<XfaMeasurement>
```

## Constructors

### `XfaMeasurement(double points)`

Initializes a measurement from a value already expressed in points.

**Parameters**

- `points` — The length in PDF points.

### `static XfaMeasurement Zero => new XfaMeasurement(0.0)`

A zero-length measurement.

## Properties

### `Points`

```csharp
double Points
```

Gets the measurement value in PDF points (1/72 inch).

## Methods

### `Parse`

__static__

```csharp
static XfaMeasurement Parse(string? text, double emPoints = 0.0, double percentBasePoints = 0.0)
```

Parses an XFA measurement string into points. Supported units: in, pt, mm, cm, pc (picas), px (treated as points at 72 dpi), em (relative to `emPoints`), and % (relative to `percentBasePoints`). A bare number with no unit is interpreted as points. Returns `Zero` when the input is null or empty.

**Parameters**

- `text` — The measurement text, e.g. "12.7mm".
- `emPoints` — The em size used to resolve "em" units, in points.
- `percentBasePoints` — The base length used to resolve "%" units, in points.

**Returns:** The parsed measurement. <exception cref="FormatException">The numeric portion could not be parsed.</exception>

### `TryParse`

__static__

```csharp
static bool TryParse(string? text, out XfaMeasurement result)
```

Attempts to parse an XFA measurement string into points, returning `Zero` rather than throwing when the input is malformed.

**Parameters**

- `text` — The measurement text.
- `result` — The parsed measurement, or `Zero` on failure.

**Returns:** `true` when parsing succeeded; otherwise `false`.

### `Equals`

```csharp
bool Equals(XfaMeasurement other) => Points.Equals(other.Points)
```

<inheritdoc />

### `Equals`

```csharp
override bool Equals(object? obj) => obj is XfaMeasurement other && Equals(other)
```

<inheritdoc />

### `GetHashCode`

```csharp
override int GetHashCode() => Points.GetHashCode()
```

<inheritdoc />

### `==`

__static__

```csharp
static bool operator ==(XfaMeasurement left, XfaMeasurement right) => left.Equals(right)
```

Equality operator.

**Parameters**

- `left` — The left operand.
- `right` — The right operand.

**Returns:** `true` when the two measurements are equal.

### `!=`

__static__

```csharp
static bool operator !=(XfaMeasurement left, XfaMeasurement right) => !left.Equals(right)
```

Inequality operator.

**Parameters**

- `left` — The left operand.
- `right` — The right operand.

**Returns:** `true` when the two measurements differ.

### `ToString`

```csharp
override string ToString() => Points.ToString("0.###", CultureInfo.InvariantCulture) + "pt"
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Xfa/Model/XfaMeasurement.cs`](../../../src/Chuvadi.Pdf.Xfa/Model/XfaMeasurement.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
