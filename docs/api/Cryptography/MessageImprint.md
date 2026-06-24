# MessageImprint

**Class** in `Chuvadi.Cryptography.Timestamps` (Cryptography)

The cryptographic commitment that a timestamp token covers.

```csharp
public sealed class MessageImprint
```

## Remarks

RFC 3161 §2.4.2: 
```
 MessageImprint ::= SEQUENCE { hashAlgorithm   AlgorithmIdentifier, hashedMessage   OCTET STRING } 
```
 For a signature timestamp embedded in CMS unsigned attrs (the typical PDF case), `HashedMessage` is the hash of the signer's signature bytes — computing `HashAlgorithm` over the SignerInfo.signature OCTET STRING content.

## Constructors

### `MessageImprint(AlgorithmIdentifier hashAlgorithm, byte[] hashedMessage)`

Initialises a new MessageImprint.

## Properties

### `HashAlgorithm`

```csharp
AlgorithmIdentifier HashAlgorithm
```

The hash algorithm used to produce `HashedMessage`.

### `HashedMessage`

```csharp
byte[] HashedMessage
```

The hash of the data the TSA was asked to timestamp.

---

_Source: [`src/Chuvadi.Cryptography/Timestamps/MessageImprint.cs`](../../../src/Chuvadi.Cryptography/Timestamps/MessageImprint.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
