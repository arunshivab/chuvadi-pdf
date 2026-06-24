# SubjectAlternativeNameExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Subject Alternative Name extension — additional naming forms for the certificate subject.

```csharp
public sealed class SubjectAlternativeNameExtension
```

## Remarks

Structure: 
```
 SubjectAltName ::= GeneralNames GeneralNames ::= SEQUENCE SIZE (1..MAX) OF GeneralName 
```
 For modern web certificates, the subject CN field is often empty or generic, and all hostnames are listed here as DNS GeneralNames. For PDF document signing certificates, this extension typically carries an rfc822Name (email address) for the signer.

## Constructors

### `SubjectAlternativeNameExtension(IList<GeneralName> names)`

Initialises a new SubjectAlternativeNameExtension.

## Properties

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.SubjectAltName
```

The OID identifying this extension.

## Methods

### `new`

```csharp
ReadOnlyCollection<GeneralName> Names => new(_names)
```

The alternative names for the subject.

### `Parse`

__static__

```csharp
static SubjectAlternativeNameExtension Parse(byte[] extnValue)
```

Parses a SubjectAltName extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/SubjectAlternativeNameExtension.cs`](../../../src/Chuvadi.Cryptography/X509/SubjectAlternativeNameExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
