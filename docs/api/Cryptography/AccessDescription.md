# AccessDescription

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

One access description inside an AuthorityInfoAccess extension.

```csharp
public sealed class AccessDescription
```

## Constructors

### `AccessDescription(ObjectIdentifier method, GeneralName location)`

Initialises a new AccessDescription.

## Properties

### `Method`

```csharp
ObjectIdentifier Method
```

The access method OID (caIssuers, ocsp, ...).

### `Location`

```csharp
GeneralName Location
```

The location of the resource.

---

_Source: [`src/Chuvadi.Cryptography/X509/AuthorityInformationAccessExtension.cs`](../../../src/Chuvadi.Cryptography/X509/AuthorityInformationAccessExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
