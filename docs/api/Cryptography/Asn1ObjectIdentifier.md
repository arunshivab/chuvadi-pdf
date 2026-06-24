# Asn1ObjectIdentifier

**Class** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Encode and decode ASN.1 OBJECT IDENTIFIER values.

```csharp
public static class Asn1ObjectIdentifier
```

## Methods

### `Write`

__static__

```csharp
static void Write(Stream output, ObjectIdentifier oid)
```

Writes an OID in DER form.

### `EncodeContent`

__static__

```csharp
static byte[] EncodeContent(ObjectIdentifier oid)
```

Encodes the OID's content octets without tag/length.

### `Read`

__static__

```csharp
static int Read(byte[] source, int offset, out ObjectIdentifier oid)
```

Reads an OID. Returns the offset just past the encoded value.

### `DecodeContent`

__static__

```csharp
static ObjectIdentifier DecodeContent(byte[] source, int contentOffset, int length, long errorOffset)
```

Decodes content octets without the tag/length wrapper.

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1ObjectIdentifier.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1ObjectIdentifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
