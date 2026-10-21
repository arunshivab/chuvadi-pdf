# OcspResponseSignatureVerifier

**Class** in `Chuvadi.Cryptography.Ocsp` (Cryptography)

Verifies the signature on a `BasicOcspResponse`.

```csharp
public static class OcspResponseSignatureVerifier
```

## Remarks

RFC 6960 §4.2.2.2 names three legitimate responders for a given subject certificate's issuer C: 
 
- C itself (direct responder), or 
- A cert issued by C with EKU `id-kp-OCSPSigning` (delegated responder), or 
- A pre-configured locally-trusted responder (not yet supported here).  

 This verifier checks the first two by trying each candidate cert in turn: the cert's issuer (if supplied) and any certs embedded inside the OCSP response. The `responderID` field is used to filter candidates.

## Methods

### `VerifyAndIdentifyResponder`

__static__

```csharp
static X509Certificate? VerifyAndIdentifyResponder(BasicOcspResponse basicResponse, X509Certificate issuerCertificate)
```

Verifies the signature on `basicResponse` and returns the certificate that signed it. Returns null when no candidate certificate produces a valid signature.

**Parameters**

- `basicResponse` — The response to verify.
- `issuerCertificate` — The issuer of the certificate that the response is about. Used as a direct responder candidate and as the authority that must have signed any delegated responder's certificate.

---

_Source: [`src/Chuvadi.Cryptography/Ocsp/OcspResponseSignatureVerifier.cs`](../../../src/Chuvadi.Cryptography/Ocsp/OcspResponseSignatureVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
