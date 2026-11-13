# AuthorityInformationAccessExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Authority Information Access extension — pointers to additional resources about the certificate's issuer (typically caIssuers and OCSP).

```csharp
public sealed class AuthorityInformationAccessExtension
```

## Remarks

Structure: 
```
 AuthorityInfoAccessSyntax ::= SEQUENCE SIZE (1..MAX) OF AccessDescription AccessDescription ::= SEQUENCE { accessMethod   OBJECT IDENTIFIER, accessLocation GeneralName } 
```

## Constructors

### `AuthorityInformationAccessExtension(IList<AccessDescription> descriptions)`

Initialises a new AuthorityInformationAccessExtension.

## Properties

### `Descriptions`

```csharp
ReadOnlyCollection<AccessDescription> Descriptions => new(_descriptions)
```

The access descriptions.

### `OcspUri`

```csharp
string? OcspUri
```

The first OCSP URI in this extension, or null when none exists.

### `CaIssuersUri`

```csharp
string? CaIssuersUri
```

The first caIssuers URI, or null when none exists.

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.AuthorityInfoAccess
```

The OID identifying this extension.

## Methods

### `Parse`

__static__

```csharp
static AuthorityInformationAccessExtension Parse(byte[] extnValue)
```

Parses an AIA extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/AuthorityInformationAccessExtension.cs`](../../../src/Chuvadi.Cryptography/X509/AuthorityInformationAccessExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
