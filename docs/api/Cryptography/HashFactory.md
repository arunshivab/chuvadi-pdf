# HashFactory

**Class** in `Chuvadi.Cryptography.Hashing` (Cryptography)

Constructs hash algorithm instances by name or by OID.

```csharp
public static class HashFactory
```

## Methods

### `Create`

__static__

```csharp
static IHashAlgorithm Create(HashAlgorithmName name)
```

Creates a hash instance for the given algorithm name.

### `CreateFromOid`

__static__

```csharp
static IHashAlgorithm CreateFromOid(ObjectIdentifier oid)
```

Creates a hash instance for the given OID. Recognises the digest-algorithm OIDs in `KnownOids`: Sha256, Sha384, Sha512. <exception cref="NotSupportedException"> Thrown when the OID is a known but unsupported hash (e.g. SHA-1, SHA-3 family). </exception> <exception cref="ArgumentException"> Thrown when the OID does not name any recognised hash algorithm. </exception>

### `IsSupportedHash`

__static__

```csharp
static bool IsSupportedHash(ObjectIdentifier oid)
```

Returns true when `oid` names a hash algorithm Chuvadi can compute.

---

_Source: [`src/Chuvadi.Cryptography/Hashing/HashFactory.cs`](../../../src/Chuvadi.Cryptography/Hashing/HashFactory.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
