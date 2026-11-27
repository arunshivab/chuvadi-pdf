# PdfLimits

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Resource limits for opening, rendering, extracting from and writing untrusted PDFs.

```csharp
public sealed class PdfLimits
```

## Remarks

Since 3.36.0 the memory, decoded-stream, operator, output-pixel and graphics-state-depth limits are <b>on by default</b> at the safe values of `Default`: an operation run with no limits specified still gets them. Set `PdfOpenOptions.Limits` (or open a `PdfWorkScope`) to a `PdfLimits` to raise or lower any of them — an instance you build keeps the safe default for every limit you do not set — or to `Unlimited` to turn them all off. A limit that would be passed throws `PdfLimitExceededException` naming it.  

 `MaxInputBytes` and `MaxPageCount` stay off by default (the caller knows its own files) and are checked when a document is opened with them (`PdfOpenOptions.Limits`). The others are checked while an operation runs inside a `PdfWorkScope`: opening, rendering (raster and SVG), text extraction, and every write/operations path (save, merge, flatten, stamp, redact, PDF/A, ...) open one; callers may open their own around any other work.  

 `MaxMemoryBytes` is an accounting ceiling over the large buffers Chuvadi allocates during one operation (decoded streams, decoded images, output pixel buffers), not a cap on the process: small objects, parsed object graphs and caches are not counted. Choose it with headroom.

## Properties

### `Default`

__static__

```csharp
static PdfLimits Default
```

Gets the safe default limits applied when none are specified (3.36.0): `MaxDecodedStreamBytes` 256 MiB, `MaxMemoryBytes` 2 GiB, `MaxOutputPixels` 200 MP, `MaxContentOperators` 50 million, `MaxGraphicsStateDepth` 256; input size and page count unlimited.

### `Unlimited`

__static__

```csharp
static PdfLimits Unlimited
```

Gets a `PdfLimits` with every limit off — the explicit opt-out of the 3.36.0 safe defaults. Use it only for input you trust.

### `MaxInputBytes`

```csharp
long? MaxInputBytes
```

Gets the largest input file accepted, in bytes. Null (default): no limit.

### `MaxPageCount`

```csharp
int? MaxPageCount
```

Gets the most pages a document may have. Null (default): no limit.

### `MaxMemoryBytes`

```csharp
long? MaxMemoryBytes
```

Gets the most bytes of large buffers (decoded streams, decoded images, output pixels) one operation may allocate in total. Defaults to `DefaultMaxMemoryBytes` (2 GiB); null for no limit.

### `MaxDecodedStreamBytes`

```csharp
long? MaxDecodedStreamBytes
```

Gets the largest size any one stream may decode to, in bytes (guards against decompression bombs). Defaults to `DefaultMaxDecodedStreamBytes` (256 MiB); null for no limit.

### `MaxContentOperators`

```csharp
long? MaxContentOperators
```

Gets the most content-stream operators one operation may process, counting forms, patterns, Type 3 glyphs and annotation appearances it draws (page complexity). Defaults to `DefaultMaxContentOperators` (50 million); null for no limit.

### `MaxOutputPixels`

```csharp
long? MaxOutputPixels
```

Gets the most pixels one output raster may have (width times height). Defaults to `DefaultMaxOutputPixels` (200 MP); null for no limit.

### `MaxGraphicsStateDepth`

```csharp
int? MaxGraphicsStateDepth
```

Gets the deepest the graphics-state stack may nest, counting every `q` not yet balanced by a `Q`, including across the forms, patterns and Type 3 glyphs a content stream draws (3.36.0, guards against a stack of a million `q`s). Defaults to `DefaultMaxGraphicsStateDepth` (256); null for no limit.

## Fields

### `DefaultMaxDecodedStreamBytes`

```csharp
const long DefaultMaxDecodedStreamBytes = 256L << 20
```

256 MiB — the default `MaxDecodedStreamBytes`.

### `DefaultMaxMemoryBytes`

```csharp
const long DefaultMaxMemoryBytes = 2L << 30
```

2 GiB — the default `MaxMemoryBytes`.

### `DefaultMaxOutputPixels`

```csharp
const long DefaultMaxOutputPixels = 200L * 1000 * 1000
```

200 megapixels — the default `MaxOutputPixels`.

### `DefaultMaxContentOperators`

```csharp
const long DefaultMaxContentOperators = 50L * 1000 * 1000
```

50 million — the default `MaxContentOperators`.

### `DefaultMaxGraphicsStateDepth`

```csharp
const int DefaultMaxGraphicsStateDepth = 256
```

256 — the default `MaxGraphicsStateDepth`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfLimits.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfLimits.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
