# TrustStore

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

A collection of trust anchors, with subject-name lookup.

```csharp
public sealed class TrustStore
```

## Constructors

### `TrustStore()`

Initialises an empty trust store.

### `TrustStore(IEnumerable<TrustAnchor> anchors)`

Initialises a trust store populated with `anchors`.

## Properties

### `Anchors`

```csharp
IReadOnlyList<TrustAnchor> Anchors => _anchors
```

The trust anchors in this store.

## Methods

### `Add`

```csharp
void Add(TrustAnchor anchor)
```

Adds a trust anchor.

### `Add`

```csharp
void Add(X509Certificate certificate)
```

Adds a trust anchor built from a trusted certificate.

### `FindBySubject`

```csharp
IEnumerable<TrustAnchor> FindBySubject(X509Name issuer)
```

Returns all trust anchors whose subject DN matches `issuer` by DER byte equality.

### `NameEquals`

__static__

```csharp
static bool NameEquals(X509Name a, X509Name b)
```

Compares two distinguished names by DER byte equality, as required by RFC 5280 §7.1 for name chaining during path validation.

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/TrustStore.cs`](../../../src/Chuvadi.Cryptography/PathValidation/TrustStore.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
