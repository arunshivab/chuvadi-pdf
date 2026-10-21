# DocumentSecurityStore

**Class** in `Chuvadi.Pdf.Signatures.Dss` (Signatures)

The Document Security Store as defined in ISO 32000-2 §12.8.4.3.

```csharp
public sealed class DocumentSecurityStore
```

## Remarks

The DSS is a dictionary on the document Catalog (key `/DSS`) carrying long-term validation material for the document's signatures: certificates, CRLs, and OCSP responses, each stored as a stream object referenced by an indirect reference inside the `/Certs`, `/CRLs`, and `/OCSPs` arrays respectively. A signer attaches these so that the document can be validated long after the issuing CA's CRL distribution points and OCSP responders are unreachable.  

 This class extracts the top-level `/Certs`, `/CRLs`, and `/OCSPs` arrays and decodes each into the corresponding Chuvadi type. Streams that fail to decode are silently skipped rather than failing the whole extraction — a single malformed CRL inside a DSS shouldn't poison the rest. The optional `/VRI` sub-dictionary (per-signature validation info, also defined in §12.8.4.3) is not yet parsed and is reserved for a future session.

## Constructors

### `DocumentSecurityStore(IList<X509Certificate> certificates, IList<CertificateList> crls, IList<OcspResponse> ocspResponses)`

Initialises a new DSS snapshot.

### `DocumentSecurityStore(IList<X509Certificate> certificates, IList<CertificateList> crls, IList<OcspResponse> ocspResponses, IDictionary<string, VriEntry> vri)`

Initialises a new DSS snapshot including a VRI sub-dictionary.

## Properties

### `Vri`

```csharp
IReadOnlyDictionary<string, VriEntry> Vri => _vri
```

Per-signature validation material keyed by upper-case SHA-1 hex of the signature's binary `/Contents` bytes (ISO 32000-2 §12.8.4.3). Empty when the DSS contains no `/VRI` sub-dictionary.

## Methods

### `new`

```csharp
ReadOnlyCollection<X509Certificate> Certificates => new(_certificates)
```

The certificates carried in the DSS `/Certs` array.

### `new`

```csharp
ReadOnlyCollection<CertificateList> Crls => new(_crls)
```

The CRLs carried in the DSS `/CRLs` array.

### `new`

```csharp
ReadOnlyCollection<OcspResponse> OcspResponses => new(_ocspResponses)
```

The OCSP responses carried in the DSS `/OCSPs` array.

### `TryFindForSignature`

```csharp
VriEntry? TryFindForSignature(byte[] signatureContents)
```

Looks up the VRI entry corresponding to a signature whose CMS `/Contents` bytes are `signatureContents`. Returns null when there is no matching entry.

**Remarks:** The VRI lookup key is the upper-case SHA-1 hex of the raw bytes inside the signature dictionary's `/Contents` value (before any hex decoding done by the PDF parser, expressed as binary).

### `TryRead`

__static__

```csharp
static DocumentSecurityStore? TryRead(PdfDictionary catalog, PdfObjectStore objects)
```

Reads the `/DSS` dictionary from `catalog` and decodes its arrays. Returns null when the catalog has no DSS.

**Parameters**

- `catalog` — The document's Catalog dictionary.
- `objects` — The object store used to resolve indirect refs.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Dss/DocumentSecurityStore.cs`](../../../src/Chuvadi.Pdf.Signatures/Dss/DocumentSecurityStore.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
