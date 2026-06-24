# X509Name

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

An X.500 distinguished name — a sequence of Relative Distinguished Names.

```csharp
public sealed class X509Name
```

## Remarks

Structure: 
```
 Name ::= CHOICE { rdnSequence  RDNSequence } RDNSequence ::= SEQUENCE OF RelativeDistinguishedName 
```
 In RFC 5280-conformant certificates the CHOICE always resolves to rdnSequence. RDN order in the encoding is significant: it goes from most-general (e.g. C=US) to most-specific (e.g. CN=John Doe). The textual presentation in `ToString` uses RFC 2253/4514 order (most-specific first) which is what humans expect.

## Constructors

### `X509Name(IList<RelativeDistinguishedName> rdns, byte[] rawEncoding)`

Initialises a new X509Name.

**Parameters**

- `rdns` — The RDNs in encoded order (most-general first).
- `rawEncoding` — The full ASN.1 TLV bytes of the original Name (preserved for signing).

## Properties

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The original DER encoding of the Name. Preserved because signature verification requires byte-identical comparison of issuer/subject names.

## Methods

### `new`

```csharp
ReadOnlyCollection<RelativeDistinguishedName> Rdns => new(_rdns)
```

The RDNs in encoded order (most-general first).

### `FindFirst`

```csharp
string? CommonName => FindFirst(KnownOids.CommonName)
```

Convenience accessor: returns the value of the first CN attribute encountered, or null if no CN exists in the DN.

### `FindFirst`

```csharp
string? FindFirst(ObjectIdentifier type)
```

Returns the first attribute value matching `type` in any RDN, or null if no such attribute exists.

### `FindAll`

```csharp
IEnumerable<string> FindAll(ObjectIdentifier type)
```

Returns all attribute values matching `type` across all RDNs in encoded order.

### `ToString`

```csharp
override string ToString()
```

Renders the DN in RFC 2253/4514 textual form (most-specific first, comma-separated).

### `Read`

__static__

```csharp
static X509Name Read(Asn1Reader reader)
```

Reads a Name from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/X509Name.cs`](../../../src/Chuvadi.Cryptography/X509/X509Name.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
