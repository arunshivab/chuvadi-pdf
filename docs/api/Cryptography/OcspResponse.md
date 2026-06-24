# OcspResponse

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

A parsed OCSP response.

```csharp
public sealed class OcspResponse
```

## Remarks

RFC 6960 §4.2.1: 
```
 OCSPResponse ::= SEQUENCE { responseStatus  OCSPResponseStatus, responseBytes   [0] EXPLICIT ResponseBytes OPTIONAL } ResponseBytes ::= SEQUENCE { responseType    OBJECT IDENTIFIER, response        OCTET STRING } 
```
 Chuvadi decodes the BasicOCSPResponse payload when present (the dominant response type in practice).

## Properties

### `Status`

```csharp
OcspResponseStatus Status
```

The response status.

### `ResponseType`

```csharp
ObjectIdentifier? ResponseType
```

The response-type OID. Null when `Status` is not Successful.

### `BasicResponse`

```csharp
BasicOcspResponse? BasicResponse
```

The parsed BasicOCSPResponse payload. Null when the response was not Successful or its responseType is not `id-pkix-ocsp-basic`.

### `RawEncoding`

```csharp
byte[] RawEncoding
```

The full DER bytes of the response.

## Methods

### `Decode`

__static__

```csharp
static OcspResponse Decode(byte[] der)
```

Parses an OCSP response from its DER encoding.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/OcspResponse.cs`](../../../src/Chuvadi.Cryptography/Ocsp/OcspResponse.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
