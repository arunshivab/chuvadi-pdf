# VriEntry

**Class** in `Chuvadi.Pdf.Signatures.Dss` (Signatures)

Per-signature validation material from the `/DSS /VRI` sub-dictionary.

```csharp
public sealed class VriEntry
```

## Remarks

ISO 32000-2 §12.8.4.3: the VRI sub-dictionary maps each signature's SHA-1 hex (upper-case, 40 characters, of the binary bytes inside the signature dictionary's `/Contents`) to a per-signature dictionary containing `/Cert`, `/CRL`, and `/OCSP` arrays of stream references — the validation material that applies specifically to that signature, rather than the document as a whole. The optional `/TS` (PDF 2.0 timestamp token) and `/TU` (creation time) entries are not yet parsed.

## Constructors

### `VriEntry(IList<X509Certificate> certificates, IList<CertificateList> crls, IList<OcspResponse> ocspResponses)`

Initialises a new VRI entry.

## Methods

### `new`

```csharp
ReadOnlyCollection<X509Certificate> Certificates => new(_certificates)
```

Certificates from the VRI entry's `/Cert` array.

### `new`

```csharp
ReadOnlyCollection<CertificateList> Crls => new(_crls)
```

CRLs from the VRI entry's `/CRL` array.

### `new`

```csharp
ReadOnlyCollection<OcspResponse> OcspResponses => new(_ocspResponses)
```

OCSP responses from the VRI entry's `/OCSP` array.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Dss/VriEntry.cs`](../../../src/Chuvadi.Pdf.Signatures/Dss/VriEntry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
