# KeyUsageExtension

**Class** in `Chuvadi.Cryptography.X509` (Cryptography)

The Key Usage extension — restricts the cryptographic operations the certified key may participate in.

```csharp
public sealed class KeyUsageExtension
```

## Remarks

Encoded as a named-bit BIT STRING; bits are numbered from the most-significant bit of the first content octet. Per RFC 5280, this extension SHOULD be marked critical when it appears.

## Constructors

### `KeyUsageExtension(KeyUsageFlags usages)`

Initialises a new KeyUsageExtension.

## Properties

### `Usages`

```csharp
KeyUsageFlags Usages
```

The combined usage flags.

### `Oid`

__static__

```csharp
static ObjectIdentifier Oid => KnownOids.KeyUsage
```

The OID identifying this extension.

## Methods

### `Has`

```csharp
bool Has(KeyUsageFlags flag) => (Usages & flag) == flag
```

True when the given flag is set.

### `Parse`

__static__

```csharp
static KeyUsageExtension Parse(byte[] extnValue)
```

Parses a KeyUsage extension from the raw extnValue bytes.

---

_Source: [`src/Chuvadi.Cryptography/X509/KeyUsageExtension.cs`](../../../src/Chuvadi.Cryptography/X509/KeyUsageExtension.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
