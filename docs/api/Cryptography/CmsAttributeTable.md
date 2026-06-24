# CmsAttributeTable

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

A collection of `CmsAttribute` values, with OID lookup and the raw encoded bytes preserved for signature verification.

```csharp
public sealed class CmsAttributeTable
```

## Remarks

For SignedAttributes (RFC 5652 §5.4), the byte sequence over which the signature is computed is the DER encoding of the SET OF Attribute — not the IMPLICIT [0] tagged form that appears on the wire. The `DerEncodedForVerification` property holds the bytes needed for that verification step.

## Constructors

### `CmsAttributeTable(IList<CmsAttribute> attributes, byte[] derEncodedForVerification)`

Initialises a new CmsAttributeTable.

## Properties

### `Count`

```csharp
int Count => _attributes.Length
```

The number of attributes in the table.

### `DerEncodedForVerification`

```csharp
byte[] DerEncodedForVerification
```

The DER encoding of the SET OF Attribute, with the universal SET tag (0x31) substituted for the IMPLICIT [0] tag that appeared on the wire. These are the bytes the signature actually covers per RFC 5652 §5.4.

## Methods

### `new`

```csharp
ReadOnlyCollection<CmsAttribute> Attributes => new(_attributes)
```

The attributes in their original order.

### `Find`

```csharp
CmsAttribute? Find(ObjectIdentifier oid)
```

Returns the first attribute matching `oid`, or null when absent.

### `FindAll`

```csharp
IEnumerable<CmsAttribute> FindAll(ObjectIdentifier oid)
```

Returns all attributes matching `oid`.

---

_Source: [`src/Chuvadi.Cryptography/Cms/CmsAttributeTable.cs`](../../../src/Chuvadi.Cryptography/Cms/CmsAttributeTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
