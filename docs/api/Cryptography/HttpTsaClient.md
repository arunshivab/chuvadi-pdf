# HttpTsaClient

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

An `ITsaClient` that POSTs RFC 3161 requests over HTTP(S) using `HttpClient`.

```csharp
public sealed class HttpTsaClient : ITsaClient, IAsyncTsaClient
```

## Remarks

Per RFC 3161 §3.4, the request body has Content-Type `application/timestamp-query` and the response Content-Type is `application/timestamp-reply`.  

 Both a TSA URL and an `HttpClient` are required. Callers own the `HttpClient` and are responsible for its disposal; the constructor does not take ownership. This allows reusing one `HttpClient` across many TSA fetches (which is the recommended .NET pattern) and lets callers configure timeouts, proxies, authentication, and so on.

## Constructors

### `HttpTsaClient(HttpClient httpClient, Uri tsaUri)`

Initialises a new HTTP TSA client.

**Parameters**

- `httpClient` — The HTTP client to use. Not owned.
- `tsaUri` — The TSA endpoint URL.

### `HttpTsaClient(HttpClient httpClient, string tsaUrl)`

Initialises a new HTTP TSA client.

**Parameters**

- `httpClient` — The HTTP client to use. Not owned.
- `tsaUrl` — The TSA endpoint URL as a string.

## Methods

### `Fetch`

```csharp
TimeStampResponse Fetch(TimeStampRequest request)
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/HttpTsaClient.cs`](../../../src/Chuvadi.Cryptography/Timestamps/HttpTsaClient.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
