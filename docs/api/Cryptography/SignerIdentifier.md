# SignerIdentifier

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

Identifies which certificate in the SignedData.certificates set produced a particular SignerInfo.

```csharp
public sealed class SignerIdentifier
```

## Remarks

Structure: 
```
 SignerIdentifier ::= CHOICE { issuerAndSerialNumber IssuerAndSerialNumber, subjectKeyIdentifier  [0] SubjectKeyIdentifier } SubjectKeyIdentifier ::= OCTET STRING 
```
 CMS v1 SignerInfo uses issuerAndSerialNumber only. CMS v3 SignerInfo (RFC 5652 §5.3) added the SKI variant to support keys without a containing certificate, though in practice almost every PDF signature still uses issuerAndSerialNumber.

## Properties

### `Kind`

```csharp
SignerIdentifierKind Kind
```

Which CHOICE variant this identifier uses.

### `IssuerAndSerial`

```csharp
IssuerAndSerialNumber? IssuerAndSerial
```

The IssuerAndSerial value, populated when Kind == IssuerAndSerial.

### `SubjectKeyIdentifier`

```csharp
byte[]? SubjectKeyIdentifier
```

The SKI bytes, populated when Kind == SubjectKeyIdentifier.

## Methods

### `Matches`

```csharp
bool Matches(X509Certificate certificate)
```

True when this identifier matches the given certificate.

### `Read`

__static__

```csharp
static SignerIdentifier Read(Asn1Reader reader)
```

Reads a SignerIdentifier from the reader.

---

_Source: [`src/Chuvadi.Cryptography/Cms/SignerIdentifier.cs`](../../../src/Chuvadi.Cryptography/Cms/SignerIdentifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
