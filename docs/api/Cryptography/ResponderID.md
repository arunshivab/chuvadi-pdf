# ResponderID

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

Identifies the responder that signed an OCSP response.

```csharp
public sealed class ResponderID
```

## Remarks

RFC 6960 §4.2.1: 
```
 ResponderID ::= CHOICE { byName  [1] EXPLICIT Name, byKey   [2] EXPLICIT KeyHash  -- SHA-1 hash of responder's pubkey BIT STRING content } 
```

## Properties

### `ByName`

```csharp
X509Name? ByName
```

The responder's distinguished name, when the responder identified itself that way.

### `ByKey`

```csharp
byte[]? ByKey
```

The SHA-1 hash of the responder's public key, when the responder identified itself by key hash.

### `IsByName`

```csharp
bool IsByName => ByName is not null
```

True iff this responder ID is the `byName` variant.

### `IsByKey`

```csharp
bool IsByKey => ByKey is not null
```

True iff this responder ID is the `byKey` variant.

## Methods

### `FromName`

__static__

```csharp
static ResponderID FromName(X509Name name)
```

Factory: responder identified by name.

### `FromKeyHash`

__static__

```csharp
static ResponderID FromKeyHash(byte[] keyHash)
```

Factory: responder identified by SHA-1 key hash.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/ResponderID.cs`](../../../src/Chuvadi.Cryptography/Ocsp/ResponderID.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
