# Asn1Tag

**Struct** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Immutable description of an ASN.1 tag.

```csharp
public readonly struct Asn1Tag : IEquatable<Asn1Tag>
```

## Remarks

An ASN.1 tag has three pieces: a class (universal / application / context-specific / private), a primitive-or-constructed flag, and a tag number. Together they uniquely identify the kind of element being encoded. This struct holds all three.

## Constructors

### `Asn1Tag(Asn1TagClass tagClass, bool isConstructed, int tagNumber)`

Initialises an Asn1Tag.

**Parameters**

- `tagClass` — The tag class.
- `isConstructed` — True for constructed encoding, false for primitive.
- `tagNumber` — The tag number. Must be non-negative. <exception cref="ArgumentOutOfRangeException">If tagNumber is negative.</exception>

## Properties

### `TagClass`

```csharp
Asn1TagClass TagClass
```

The tag class.

### `IsConstructed`

```csharp
bool IsConstructed
```

True for constructed encoding (contents are themselves encoded values).

### `TagNumber`

```csharp
int TagNumber
```

The tag number.

## Methods

### `Primitive`

__static__

```csharp
static Asn1Tag Primitive(Asn1UniversalTag tag)
```

Builds a universal-class primitive tag for the given universal type.

### `Constructed`

__static__

```csharp
static Asn1Tag Constructed(Asn1UniversalTag tag)
```

Builds a universal-class constructed tag for the given universal type. Used for SEQUENCE, SET, and the constructed encodings of strings.

### `ContextSpecific`

__static__

```csharp
static Asn1Tag ContextSpecific(int tagNumber, bool isConstructed)
```

Builds a context-specific tag with the given number and constructed flag.

### `Equals`

```csharp
bool Equals(Asn1Tag other)
```

<inheritdoc/>

### `Equals`

```csharp
override bool Equals(object? obj) => obj is Asn1Tag other && Equals(other)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode() => HashCode.Combine(TagClass, IsConstructed, TagNumber)
```

<inheritdoc/>

### `==`

__static__

```csharp
static bool operator ==(Asn1Tag left, Asn1Tag right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(Asn1Tag left, Asn1Tag right) => !left.Equals(right)
```

Inequality operator.

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Tag.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Tag.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
