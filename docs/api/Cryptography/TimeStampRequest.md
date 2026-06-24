# TimeStampRequest

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

An RFC 3161 Time-Stamp Protocol request, ready to POST to a TSA.

```csharp
public sealed class TimeStampRequest
```

## Remarks

ASN.1 structure (RFC 3161 §2.4.1): 
```
 TimeStampReq ::= SEQUENCE { version       INTEGER  { v1(1) }, messageImprint MessageImprint, reqPolicy     TSAPolicyId  OPTIONAL, nonce         INTEGER  OPTIONAL, certReq       BOOLEAN  DEFAULT FALSE, extensions    [0] IMPLICIT Extensions  OPTIONAL } 
```
  

 Built via `ForData` or `ForDigest`. The MIME type when POSTing is `application/timestamp-query`; the response comes back as `application/timestamp-reply`.

## Properties

### `MessageImprint`

```csharp
MessageImprint MessageImprint
```

The hash algorithm + the digest being time-stamped.

### `Nonce`

```csharp
BigInteger? Nonce
```

Optional nonce for replay-protection; null when not requested.

### `CertReq`

```csharp
bool CertReq
```

When true, the TSA is asked to include its cert in the response.

### `ReqPolicy`

```csharp
ObjectIdentifier? ReqPolicy
```

Optional requested policy OID; null when accepting the TSA's default.

## Methods

### `Encode`

```csharp
byte[] Encode()
```

DER-encodes this request per RFC 3161 §2.4.1.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/TimeStampRequest.cs`](../../../src/Chuvadi.Cryptography/Timestamps/TimeStampRequest.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
