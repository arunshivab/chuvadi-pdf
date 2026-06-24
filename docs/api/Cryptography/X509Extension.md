# X509Extension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

A single X.509 v3 extension — an OID, a criticality flag, and an opaque OCTET STRING value whose contents are defined per OID.

```csharp
public sealed class X509Extension
```

## Remarks

Structure: 
```
 Extension ::= SEQUENCE { extnID     OBJECT IDENTIFIER, critical   BOOLEAN DEFAULT FALSE, extnValue  OCTET STRING } 
```
 The criticality flag has security significance: per RFC 5280 §4.2, a relying party MUST reject a certificate with a critical extension it does not understand. The raw extnValue bytes are preserved; specialised parsers (BasicConstraintsExtension, KeyUsageExtension, etc.) interpret them on demand.

## Constructors

### `X509Extension(ObjectIdentifier oid, bool critical, byte[] value)`

Initialises a new X509Extension.

## Properties

### `Oid`

```csharp
ObjectIdentifier Oid
```

The extension OID.

### `Critical`

```csharp
bool Critical
```

True when this extension is marked critical.

### `Value`

```csharp
byte[] Value
```

The raw extnValue contents (the bytes inside the OCTET STRING wrapper).

## Methods

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

### `Read`

__static__

```csharp
static X509Extension Read(Asn1Reader reader)
```

Reads an X509Extension from a reader positioned at its SEQUENCE.

---

_Source: [`src/Chuvadi.Cryptography/X509/X509Extension.cs`](../../../src/Chuvadi.Cryptography/X509/X509Extension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
