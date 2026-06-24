# IssuerAndSerialNumber

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

Identifies an X.509 certificate by its issuer's distinguished name and the certificate's serial number.

```csharp
public sealed class IssuerAndSerialNumber
```

## Remarks

Structure: 
```
 IssuerAndSerialNumber ::= SEQUENCE { issuer        Name, serialNumber  CertificateSerialNumber } CertificateSerialNumber ::= INTEGER 
```
 The pair (issuer DN, serial number) uniquely identifies a certificate — issuers are required by RFC 5280 to never reuse a serial number within their domain. This is the most common SignerIdentifier form in CMS signatures used by PDFs today, including all signatures produced by Adobe Acrobat.

## Constructors

### `IssuerAndSerialNumber(X509Name issuer, BigInteger serialNumber)`

Initialises a new IssuerAndSerialNumber.

## Properties

### `Issuer`

```csharp
X509Name Issuer
```

The issuer distinguished name.

### `SerialNumber`

```csharp
BigInteger SerialNumber
```

The certificate serial number.

## Methods

### `Matches`

```csharp
bool Matches(X509Certificate certificate)
```

True when this identifier matches the given certificate.

**Remarks:** Matching uses byte-identical comparison of the issuer Name encoding (per RFC 5280 §7.1) and arithmetic equality of the serial number.

### `ToString`

```csharp
override string ToString() => $"
```

<inheritdoc/>

### `Read`

__static__

```csharp
static IssuerAndSerialNumber Read(Asn1Reader reader)
```

Reads an IssuerAndSerialNumber from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/IssuerAndSerialNumber.cs`](../../../src/Chuvadi.Cryptography/Cms/IssuerAndSerialNumber.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
