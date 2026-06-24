# EcPoint

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

A point on a short Weierstrass elliptic curve in affine coordinates.

```csharp
public sealed class EcPoint
```

## Remarks

The point at infinity (group identity) is represented by `IsInfinity` being true; the `X` and `Y` values are irrelevant in that case.

## Properties

### `Curve`

```csharp
EcCurve Curve
```

The curve this point lives on.

### `X`

```csharp
BigInteger X
```

The affine x coordinate (undefined when `IsInfinity`).

### `Y`

```csharp
BigInteger Y
```

The affine y coordinate (undefined when `IsInfinity`).

### `IsInfinity`

```csharp
bool IsInfinity
```

True when this point is the group identity (point at infinity).

## Methods

### `Create`

__static__

```csharp
static EcPoint Create(EcCurve curve, BigInteger x, BigInteger y)
```

Creates an affine point on `curve`. <exception cref="ArgumentException">If (x, y) is not on the curve.</exception>

### `Infinity`

__static__

```csharp
static EcPoint Infinity(EcCurve curve)
```

The point at infinity on `curve`.

### `Generator`

__static__

```csharp
static EcPoint Generator(EcCurve curve)
```

The generator (base point) of `curve`.

### `Negate`

```csharp
EcPoint Negate()
```

Returns this point's additive inverse (negation flips y).

### `Add`

```csharp
EcPoint Add(EcPoint other)
```

Returns `this + other`.

### `Double`

```csharp
EcPoint Double()
```

Returns `2 * this` (point doubling).

### `Multiply`

```csharp
EcPoint Multiply(BigInteger k)
```

Returns `k * this` via double-and-add. `k` must be non-negative.

### `Mod`

__static__

```csharp
static BigInteger Mod(BigInteger value, BigInteger m)
```

Returns `value mod m`, normalised to `[0, m)`.

### `ModInverse`

__static__

```csharp
static BigInteger ModInverse(BigInteger a, BigInteger m)
```

Returns the modular multiplicative inverse of `a` mod `m`, via the extended Euclidean algorithm. <exception cref="InvalidOperationException">If gcd(a, m) != 1.</exception>

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcPoint.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcPoint.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
