# CmsDecoder

**Class** in `Chuvadi.Cryptography.Cms` (Cryptography)

Decodes CMS / PKCS#7 byte streams into structured Chuvadi objects.

```csharp
public static class CmsDecoder
```

## Methods

### `DecodeContentInfo`

__static__

```csharp
static ContentInfo DecodeContentInfo(byte[] cms)
```

Decodes `cms` as a ContentInfo. Use this when you have the bytes from a PDF /Contents field or a PKCS#7 file.

### `DecodeSignedData`

__static__

```csharp
static SignedData DecodeSignedData(byte[] cms)
```

Decodes `cms` and returns the inner SignedData. Throws when the ContentInfo carries a different content type.

---

_Source: [`src/Chuvadi.Cryptography/Cms/CmsDecoder.cs`](../../../src/Chuvadi.Cryptography/Cms/CmsDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
