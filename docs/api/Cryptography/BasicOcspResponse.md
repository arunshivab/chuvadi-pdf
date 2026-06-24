# BasicOcspResponse

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

A parsed BasicOCSPResponse — the typical OCSP response payload.

```csharp
public sealed class BasicOcspResponse
```

## Remarks

RFC 6960 §4.2.1: 
```
 BasicOCSPResponse ::= SEQUENCE { tbsResponseData       ResponseData, signatureAlgorithm    AlgorithmIdentifier, signature             BIT STRING, certs                 [0] EXPLICIT SEQUENCE OF Certificate OPTIONAL } ResponseData ::= SEQUENCE { version               [0] EXPLICIT INTEGER DEFAULT v1, responderID           ResponderID, producedAt            GeneralizedTime, responses             SEQUENCE OF SingleResponse, responseExtensions    [1] EXPLICIT Extensions OPTIONAL } 
```

## Properties

### `Version`

```csharp
int Version
```

OCSP version (default 1).

### `ResponderId`

```csharp
ResponderID ResponderId
```

Identifies the responder.

### `ProducedAt`

```csharp
DateTimeOffset ProducedAt
```

The time the response was produced.

### `TbsRawEncoding`

```csharp
byte[] TbsRawEncoding
```

The raw DER bytes of `tbsResponseData`, hashed for signature verification.

### `SignatureAlgorithm`

```csharp
AlgorithmIdentifier SignatureAlgorithm
```

Signature algorithm identifier.

### `SignatureValue`

```csharp
BitStringValue SignatureValue
```

The signature over `TbsRawEncoding`.

## Methods

### `new`

```csharp
ReadOnlyCollection<SingleResponse> Responses => new(_responses)
```

The per-certificate status entries.

### `new`

```csharp
ReadOnlyCollection<X509Certificate> Certificates => new(_certs)
```

Optional certificates attached to the response. When the responder is delegated, this typically contains the responder's signing cert.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/BasicOcspResponse.cs`](../../../src/Chuvadi.Cryptography/Ocsp/BasicOcspResponse.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
