# CmsAttribute

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

A generic CMS Attribute — an OID identifying the attribute type, plus a SET of one or more values whose content is defined per OID.

```csharp
public sealed class CmsAttribute
```

## Remarks

Structure: 
```
 Attribute ::= SEQUENCE { attrType   OBJECT IDENTIFIER, attrValues SET OF AttributeValue } AttributeValue ::= ANY 
```
 Each value is preserved as its complete encoded TLV bytes so callers can decode them with whatever specific parser the attrType demands. Common attrTypes registered in `Chuvadi.Cryptography.Oids.KnownOids`: ContentType, MessageDigest, SigningTime, SigningCertificate(V2), SignatureTimeStampToken.

## Constructors

### `CmsAttribute(ObjectIdentifier type, IList<byte[]> values)`

Initialises a new CmsAttribute.

## Properties

### `Type`

```csharp
ObjectIdentifier Type
```

The attribute type OID.

### `IsSingleValued`

```csharp
bool IsSingleValued => _values.Length == 1
```

True when the attribute carries exactly one value (the typical case).

## Methods

### `new`

```csharp
ReadOnlyCollection<byte[]> Values => new(_values)
```

The complete encoded TLV bytes of each value in the order they appeared in the SET.

### `Read`

__static__

```csharp
static CmsAttribute Read(Asn1Reader reader)
```

Reads a CmsAttribute from a reader at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/Cms/CmsAttribute.cs`](../../../src/Chuvadi.Cryptography/Cms/CmsAttribute.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
