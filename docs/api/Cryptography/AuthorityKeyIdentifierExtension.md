# AuthorityKeyIdentifierExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Authority Key Identifier extension — identifies the public key whose holder signed this certificate. Used for issuer lookup during path building.

```csharp
public sealed class AuthorityKeyIdentifierExtension
```

## Remarks

Structure: 
```
 AuthorityKeyIdentifier ::= SEQUENCE { keyIdentifier             [0] IMPLICIT KeyIdentifier OPTIONAL, authorityCertIssuer       [1] IMPLICIT GeneralNames OPTIONAL, authorityCertSerialNumber [2] IMPLICIT CertificateSerialNumber OPTIONAL } 
```
 At least one of the three fields is typically present. Most certificates supply only keyIdentifier (matching the issuer's SubjectKeyIdentifier). Chuvadi exposes all three; the GeneralNames payload is kept raw because the GeneralName CHOICE has its own non-trivial decoder.

## Properties

### `KeyIdentifier`

```csharp
byte[]? KeyIdentifier
```

The issuer's key identifier (typically SHA-1 of its SubjectPublicKey).

### `AuthorityCertIssuerRaw`

```csharp
byte[]? AuthorityCertIssuerRaw
```

The raw bytes of authorityCertIssuer (a GeneralNames CHOICE).

### `AuthorityCertSerialNumber`

```csharp
BigInteger? AuthorityCertSerialNumber
```

The serial number of the authority certificate.

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.AuthorityKeyIdentifier
```

The OID identifying this extension.

## Methods

### `Parse`

__static__

```csharp
static AuthorityKeyIdentifierExtension Parse(byte[] extnValue)
```

Parses an AuthorityKeyIdentifier extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/AuthorityKeyIdentifierExtension.cs`](../../../src/Chuvadi.Cryptography/X509/AuthorityKeyIdentifierExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
