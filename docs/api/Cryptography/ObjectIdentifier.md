# ObjectIdentifier

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

An ASN.1 OBJECT IDENTIFIER — an ordered sequence of non-negative arcs.

```csharp
public sealed class ObjectIdentifier : IEquatable<ObjectIdentifier>
```

## Remarks

First arc is constrained to 0, 1, or 2 (X.690 §8.19.4). When first arc is 0 or 1 the second arc is 0..39. When first arc is 2 the second arc may be any non-negative integer. The encoding packs the first two arcs into a single value: `40 * arc1 + arc2`, then each subsequent arc is encoded as a base-128 big-endian SubIdentifier with continuation bits.

## Constructors

### `ObjectIdentifier(params long[] arcs)`

Initialises an OID from its arcs.

### `ObjectIdentifier(string dotted) : this(ParseDotted(dotted))`

Initialises an OID from dotted form (e.g. "1.2.840.113549.1.7.2").

## Properties

### `Dotted`

```csharp
string Dotted => _dotted
```

The OID in dotted-decimal form.

## Methods

### `=>`

```csharp
long[] Arcs => (long[])_arcs.Clone()
```

The arcs as an array (defensive copy).

### `ToString`

```csharp
override string ToString() => _dotted
```

<inheritdoc/>

### `Equals`

```csharp
bool Equals(ObjectIdentifier? other)
```

<inheritdoc/>

### `Equals`

```csharp
override bool Equals(object? obj) => Equals(obj as ObjectIdentifier)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode() => _dotted.GetHashCode(StringComparison.Ordinal)
```

<inheritdoc/>

### `==`

__static__

```csharp
static bool operator ==(ObjectIdentifier? left, ObjectIdentifier? right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(ObjectIdentifier? left, ObjectIdentifier? right)
```

Inequality operator.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1ObjectIdentifier.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1ObjectIdentifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
