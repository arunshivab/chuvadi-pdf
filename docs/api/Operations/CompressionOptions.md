# CompressionOptions

**Record** in `Chuvadi.Pdf.Operations` (Operations)

Options controlling `PdfCompressor.Compress`.

```csharp
public sealed record CompressionOptions
```

## Properties

### `RecompressImages`

```csharp
bool RecompressImages
```

When true, eligible raster images (8-bit RGB or grayscale, stored raw or Flate-compressed, without transparency) are re-encoded as JPEG at `JpegQuality`. This is lossy and off by default.

### `JpegQuality`

```csharp
int JpegQuality
```

JPEG quality (1–100, IJG convention) used when `RecompressImages` is enabled. Default 75.

### `MinStreamLengthToCompress`

```csharp
int MinStreamLengthToCompress
```

Minimum raw stream length, in bytes, worth Flate-compressing. Streams shorter than this are left untouched. Default 64.

### `MinImagePixelsToRecompress`

```csharp
int MinImagePixelsToRecompress
```

Minimum pixel count (width × height) for an image to be considered for JPEG recompression. Default 4096 (e.g. 64×64).

### `AllowSignedRewrite`

```csharp
bool AllowSignedRewrite
```

When false (the default), a digitally signed document is not rewritten: `PdfCompressor.Compress` returns a result whose `CompressionResult.SkipReason` is `CompressionSkipReason.Signed` and writes nothing, because a full rewrite invalidates the signature byte ranges. Set to true to rewrite anyway, accepting that existing signatures will break.

### `AllowEncryptedRewrite`

```csharp
bool AllowEncryptedRewrite
```

When false (the default), an encrypted document is not rewritten: `PdfCompressor.Compress` returns a result whose `CompressionResult.SkipReason` is `CompressionSkipReason.Encrypted` and writes nothing, because the reader exposes decrypted content and the rewrite would emit the document without encryption. Set to true to rewrite the decrypted content anyway.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
