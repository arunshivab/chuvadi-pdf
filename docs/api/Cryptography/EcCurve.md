# EcCurve

**Class** in `Chuvadi.Cryptography.PublicKey` (Cryptography)

A named elliptic curve over a prime field — the parameters needed to perform ECDSA verification.

```csharp
public sealed class EcCurve
```

## Remarks

All Chuvadi-supported curves use the short Weierstrass equation `y² = x³ + a·x + b (mod p)` with `a = -3 (mod p)` per NIST recommendations.

## Properties

### `Name`

```csharp
string Name
```

Friendly name (e.g. "P-256").

### `Oid`

```csharp
ObjectIdentifier Oid
```

The OID that identifies this curve in SubjectPublicKeyInfo parameters.

### `P`

```csharp
BigInteger P
```

Prime field modulus.

### `A`

```csharp
BigInteger A
```

Curve coefficient a (= -3 mod p for NIST curves).

### `B`

```csharp
BigInteger B
```

Curve coefficient b.

### `Gx`

```csharp
BigInteger Gx
```

Base point x coordinate.

### `Gy`

```csharp
BigInteger Gy
```

Base point y coordinate.

### `N`

```csharp
BigInteger N
```

Order of the base point.

### `FieldSizeBytes`

```csharp
int FieldSizeBytes
```

Field size in bytes (ceiling of bit length divided by 8).

## Methods

### `IsOnCurve`

```csharp
bool IsOnCurve(BigInteger x, BigInteger y)
```

True when `x` and `y` satisfy the curve equation.

### `FromOid`

__static__

```csharp
static EcCurve FromOid(ObjectIdentifier oid)
```

Resolves a curve by its OID. <exception cref="NotSupportedException">Thrown for curves Chuvadi doesn't implement.</exception>

---

_Source: [`src/Chuvadi.Cryptography/PublicKey/EcCurve.cs`](../../../src/Chuvadi.Cryptography/PublicKey/EcCurve.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
