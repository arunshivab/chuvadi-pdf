# AttributeTypeAndValue

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

One attribute within a RelativeDistinguishedName — an OID identifying the attribute type plus its value.

```csharp
public sealed class AttributeTypeAndValue
```

## Remarks

Structure: 
```
 AttributeTypeAndValue ::= SEQUENCE { type   OBJECT IDENTIFIER, value  ANY DEFINED BY type } 
```
 In practice the value is almost always one of the directory string types (UTF8String, PrintableString, T61String, BMPString) or IA5String for emailAddress. Chuvadi exposes both the original tag-class and the decoded string so callers can preserve canonical encodings when re-serialising.

## Constructors

### `AttributeTypeAndValue(ObjectIdentifier type, string value, Asn1UniversalTag valueTag)`

Initialises a new AttributeTypeAndValue.

## Properties

### `Type`

```csharp
ObjectIdentifier Type
```

The attribute type OID (e.g. KnownOids.CommonName).

### `Value`

```csharp
string Value
```

The decoded string value.

### `ValueTag`

```csharp
Asn1UniversalTag ValueTag
```

The original ASN.1 string tag of the encoded value.

## Methods

### `GetShortName`

```csharp
string ShortName => GetShortName(Type)
```

The short attribute name (e.g. "CN", "O") if registered, otherwise the OID dotted form.

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

### `Read`

__static__

```csharp
static AttributeTypeAndValue Read(Asn1Reader reader)
```

Reads an AttributeTypeAndValue from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/AttributeTypeAndValue.cs`](../../../src/Chuvadi.Cryptography/X509/AttributeTypeAndValue.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
