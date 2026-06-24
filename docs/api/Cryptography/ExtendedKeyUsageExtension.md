# ExtendedKeyUsageExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Extended Key Usage extension — additional or alternative purposes for which the certified public key may be used.

```csharp
public sealed class ExtendedKeyUsageExtension
```

## Remarks

Structure: 
```
 ExtKeyUsageSyntax ::= SEQUENCE SIZE (1..MAX) OF KeyPurposeId KeyPurposeId ::= OBJECT IDENTIFIER 
```
 Common purposes registered in `KnownOids`: ServerAuth, ClientAuth, CodeSigning, EmailProtection, TimeStamping, OcspSigning, DocumentSigning.

## Constructors

### `ExtendedKeyUsageExtension(IList<ObjectIdentifier> purposes)`

Initialises a new ExtendedKeyUsageExtension.

## Properties

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.ExtKeyUsage
```

The OID identifying this extension.

## Methods

### `new`

```csharp
ReadOnlyCollection<ObjectIdentifier> Purposes => new(_purposes)
```

The set of key purpose OIDs.

### `Allows`

```csharp
bool Allows(ObjectIdentifier purpose)
```

True when the given purpose OID is present.

### `Parse`

__static__

```csharp
static ExtendedKeyUsageExtension Parse(byte[] extnValue)
```

Parses an ExtendedKeyUsage extension from raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/ExtendedKeyUsageExtension.cs`](../../../src/Chuvadi.Cryptography/X509/ExtendedKeyUsageExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
