# RelativeDistinguishedName

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

A SET of one or more attributes that together form one component of a DN.

```csharp
public sealed class RelativeDistinguishedName
```

## Remarks

Structure: 
```
 RelativeDistinguishedName ::= SET SIZE (1..MAX) OF AttributeTypeAndValue 
```
 In real-world certificates an RDN almost always contains a single attribute; multi-valued RDNs are rare but legal. Order within the SET is not significant for DN comparison but Chuvadi preserves the encoded order for re-serialisation.

## Constructors

### `RelativeDistinguishedName(IList<AttributeTypeAndValue> attributes)`

Initialises a new RDN.

## Methods

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

### `Read`

__static__

```csharp
static RelativeDistinguishedName Read(Asn1Reader reader)
```

Reads an RDN from a reader positioned at its SET.

---

_Source: [`src/Chuvadi.Cryptography/X509/RelativeDistinguishedName.cs`](../../../src/Chuvadi.Cryptography/X509/RelativeDistinguishedName.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
