# LtvMaterialDiscovery

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Walks a certificate chain and fetches the validation material (CRLs, OCSP responses) advertised by each certificate's extensions. Used to populate `LtvOptions` without making the caller wire up HTTP fetches by hand.

```csharp
public static class LtvMaterialDiscovery
```

## Remarks

For each cert in the chain (leaf first): 
 
- CRL Distribution Points (RFC 5280 §4.2.1.13) HTTP URLs are fetched and decoded. 
- Authority Information Access (RFC 5280 §4.2.2.1) OCSP URLs are POSTed an OCSP request (built by `ocspRequestFactory` if supplied; otherwise OCSP is skipped) and the response is decoded.   

 Per-URL failures are tolerated: discovery collects what it can and reports failures via `DiscoveryHooks.OnFetchFailed`.

## Methods

### `DiscoverAsync`

__static__

```csharp
static async Task<LtvOptions> DiscoverAsync(IReadOnlyList<X509Certificate> chain, HttpClient httpClient, Func<X509Certificate, X509Certificate, byte[]>? ocspRequestFactory = null, DiscoveryHooks? hooks = null, CancellationToken cancellationToken = default)
```

Discovers LTV material for the signer's chain.

**Parameters**

- `chain` — The chain ordered leaf → intermediates → root.
- `httpClient` — HTTP client used to fetch CRLs and OCSP responses.
- `ocspRequestFactory` — Optional. Builds an OCSP request body for (cert, issuer). When null, OCSP fetching is skipped.
- `hooks` — Optional discovery hooks.
- `cancellationToken` — Cancellation token.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/LtvMaterialDiscovery.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/LtvMaterialDiscovery.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
