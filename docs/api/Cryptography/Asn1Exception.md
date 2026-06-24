# Asn1Exception

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Raised when an ASN.1 decoder encounters malformed or non-conforming input.

```csharp
public sealed class Asn1Exception : Exception
```

## Remarks

The decoder never throws NullReferenceException, IndexOutOfRangeException, or InvalidCastException on malformed input; every defect surface as an Asn1Exception with a message describing the violation and (where applicable) the byte offset at which it was detected.

## Constructors

### `Asn1Exception()`

Initialises a new `Asn1Exception` with no message.

### `Asn1Exception(string message) : base(message)`

Initialises a new `Asn1Exception`.

### `Asn1Exception(string message, Exception innerException)`

Initialises a new `Asn1Exception` with an inner exception.

### `Asn1Exception(string message, long byteOffset)`

Initialises a new `Asn1Exception` annotated with a byte offset.

## Properties

### `ByteOffset`

```csharp
long ByteOffset
```

The byte offset at which the defect was detected, or -1 if unknown.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1Exception.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1Exception.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
