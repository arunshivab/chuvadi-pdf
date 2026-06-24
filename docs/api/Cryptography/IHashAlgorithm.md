# IHashAlgorithm

**Interface** in `Chuvadi.Cryptography.Hashing` (Cryptography)

A streaming cryptographic hash function.

```csharp
public interface IHashAlgorithm
```

## Remarks

Usage pattern: construct an instance, call `Update` zero or more times to feed bytes, then call `Finish` once to obtain the digest. After `Finish` the instance is consumed; further calls throw. To hash a second message construct a new instance, or call `Reset`.

---

_Source: [`src/Chuvadi.Cryptography/Hashing/IHashAlgorithm.cs`](../../../src/Chuvadi.Cryptography/Hashing/IHashAlgorithm.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
