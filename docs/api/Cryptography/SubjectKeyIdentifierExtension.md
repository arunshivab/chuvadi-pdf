# SubjectKeyIdentifierExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Subject Key Identifier extension — a short octet string identifying the certificate's public key, used to find issuer certificates during path building.

```csharp
public sealed class SubjectKeyIdentifierExtension
```

## Remarks

Structure: 
```
 SubjectKeyIdentifier ::= KeyIdentifier KeyIdentifier ::= OCTET STRING 
```
 The most common derivation method is the SHA-1 hash of the SubjectPublicKey BIT STRING contents (RFC 5280 §4.2.1.2 method 1), but the field is opaque and any unique identifier is permitted.

## Constructors

### `SubjectKeyIdentifierExtension(byte[] keyIdentifier)`

Initialises a new SubjectKeyIdentifierExtension.

## Properties

### `KeyIdentifier`

```csharp
byte[] KeyIdentifier
```

The key identifier bytes.

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.SubjectKeyIdentifier
```

The OID identifying this extension.

## Methods

### `Parse`

__static__

```csharp
static SubjectKeyIdentifierExtension Parse(byte[] extnValue)
```

Parses a SubjectKeyIdentifier extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/SubjectKeyIdentifierExtension.cs`](../../../src/Chuvadi.Cryptography/X509/SubjectKeyIdentifierExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
