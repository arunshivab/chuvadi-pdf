# OidNameLookup

**Class** in `Chuvadi.Cryptography.Oids` (Cryptography)

Maps an `ObjectIdentifier` to the friendly name from `KnownOids` for diagnostics and error messages.

```csharp
public static class OidNameLookup
```

## Methods

### `GetName`

__static__

```csharp
static string GetName(ObjectIdentifier oid)
```

Returns the friendly name (e.g. "Sha256WithRsa") for a known OID, or the dotted form if the OID isn't in the registry.

### `IsKnown`

__static__

```csharp
static bool IsKnown(ObjectIdentifier oid)
```

Returns true when the OID is one of Chuvadi's recognised constants.

---

_Source: [`src/Chuvadi.Cryptography/Oids/OidNameLookup.cs`](../../../src/Chuvadi.Cryptography/Oids/OidNameLookup.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
