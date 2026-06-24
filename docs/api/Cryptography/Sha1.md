# Sha1

**Class** in `Chuvadi.Cryptography.PathValidation` (Cryptography)

SHA-1 used only for lookup-key purposes mandated by external specs: RFC 6960 §4.1.1 (OCSP CertID IssuerNameHash / IssuerKeyHash) and ISO 32000-2 §12.8.4.3 (PDF DSS VRI keys). SHA-1 is otherwise deprecated in Chuvadi and refused by `HashFactory`; this class exists because those specs require SHA-1 specifically for non-security-critical lookups (the surrounding signatures are still verified with strong algorithms).

```csharp
public static class Sha1
```

## Methods

### `Compute`

__static__

```csharp
static byte[] Compute(byte[] data)
```

---

_Source: [`src/Chuvadi.Cryptography/PathValidation/Sha1ForOcsp.cs`](../../../src/Chuvadi.Cryptography/PathValidation/Sha1ForOcsp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
