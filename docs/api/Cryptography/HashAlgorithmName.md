# HashAlgorithmName

**Enum** in `Chuvadi.Cryptography.Hashing` (Cryptography)

Enumeration of the hash algorithms Chuvadi implements.

```csharp
public enum HashAlgorithmName
```

## Remarks

SHA-1 is deliberately excluded. It is deprecated for new digital signatures per RFC 8017 §8.1 and prohibited by eIDAS qualified-signature regulations. Verification of legacy SHA-1 signatures is intentionally unsupported.

## Values

| Name | Description |
|---|---|
| `Sha256` | SHA-256 (FIPS 180-4 §6.2). 256-bit digest. |
| `Sha384` | SHA-384 (FIPS 180-4 §6.5). 384-bit digest using SHA-512 internals. |
| `Sha512` | SHA-512 (FIPS 180-4 §6.4). 512-bit digest. |

---

_Source: [`src/Chuvadi.Cryptography/Hashing/HashAlgorithmName.cs`](../../../src/Chuvadi.Cryptography/Hashing/HashAlgorithmName.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
