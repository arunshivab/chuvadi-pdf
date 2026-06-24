# AlgorithmIdentifier

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

An ASN.1 AlgorithmIdentifier as defined by RFC 5280 §4.1.1.2.

```csharp
public sealed class AlgorithmIdentifier : IEquatable<AlgorithmIdentifier>
```

## Remarks

Structure: 
```
 AlgorithmIdentifier ::= SEQUENCE { algorithm   OBJECT IDENTIFIER, parameters  ANY DEFINED BY algorithm OPTIONAL } 
```
 The parameters field is algorithm-specific. For RSA encryption the parameters are explicit NULL (RFC 3279); for ECDSA they are absent (RFC 5480); for RSA-PSS they are a complex SEQUENCE (RFC 8017). Chuvadi preserves the parameters as raw encoded bytes so each algorithm can decode them on demand.

## Constructors

### `AlgorithmIdentifier(ObjectIdentifier algorithm, byte[]? parameters)`

Initialises a new AlgorithmIdentifier.

**Parameters**

- `algorithm` — The algorithm OID.
- `parameters` — The raw parameter bytes (the complete TLV), or null/empty for absent.

## Properties

### `Algorithm`

```csharp
ObjectIdentifier Algorithm
```

The algorithm OID.

### `Parameters`

```csharp
byte[] Parameters
```

The raw parameter bytes (empty when parameters are absent).

### `ParametersAreAbsent`

```csharp
bool ParametersAreAbsent => Parameters.Length == 0
```

True when parameters are absent (ECDSA and most modern key types).

## Methods

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

### `Equals`

```csharp
bool Equals(AlgorithmIdentifier? other)
```

<inheritdoc/>

### `Equals`

```csharp
override bool Equals(object? obj) => Equals(obj as AlgorithmIdentifier)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode() => Algorithm.GetHashCode()
```

<inheritdoc/>

### `Read`

__static__

```csharp
static AlgorithmIdentifier Read(Asn1Reader reader)
```

Reads an AlgorithmIdentifier from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/AlgorithmIdentifier.cs`](../../../src/Chuvadi.Cryptography/X509/AlgorithmIdentifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
