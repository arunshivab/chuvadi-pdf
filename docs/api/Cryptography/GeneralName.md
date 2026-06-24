# GeneralName

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

One alternative naming form for a certificate subject or other entity.

```csharp
public sealed class GeneralName
```

## Remarks

Structure: 
```
 GeneralName ::= CHOICE { otherName                 [0] OtherName, rfc822Name                [1] IA5String, dNSName                   [2] IA5String, x400Address               [3] ORAddress, directoryName             [4] Name, ediPartyName              [5] EDIPartyName, uniformResourceIdentifier [6] IA5String, iPAddress                 [7] OCTET STRING, registeredID              [8] OBJECT IDENTIFIER } 
```

## Properties

### `Kind`

```csharp
GeneralNameKind Kind
```

Which CHOICE variant this name represents.

### `StringValue`

```csharp
string? StringValue
```

The string value for the rfc822Name, dNSName, and URI variants.

### `RawValue`

```csharp
byte[]? RawValue
```

The raw value for variants without a structured Chuvadi representation.

### `DirectoryName`

```csharp
X509Name? DirectoryName
```

The decoded value for the directoryName variant.

### `OidValue`

```csharp
ObjectIdentifier? OidValue
```

The OID value for the registeredID variant.

## Methods

### `Read`

__static__

```csharp
static GeneralName Read(Asn1Reader reader)
```

Reads the next GeneralName from the reader.

### `ReadSequence`

__static__

```csharp
static List<GeneralName> ReadSequence(Asn1Reader reader)
```

Reads a SEQUENCE OF GeneralName (i.e. a GeneralNames structure).

---

_Source: [`src/Chuvadi.Cryptography/X509/GeneralName.cs`](../../../src/Chuvadi.Cryptography/X509/GeneralName.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
