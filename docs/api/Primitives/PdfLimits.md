# PdfLimits

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Resource limits for opening, rendering and extracting untrusted PDFs. Every limit is off (null) by default. A limit that would be passed throws `PdfLimitExceededException` naming it.

```csharp
public sealed class PdfLimits
```

## Remarks

`MaxInputBytes` and `MaxPageCount` are checked when a document is opened with them (`PdfOpenOptions.Limits`). The others are checked while an operation runs inside a `PdfWorkScope`: `PageRasterizer` and `TextExtractor` open one per call with the limits the document was opened with, and callers may open their own around any other work.  

 `MaxMemoryBytes` is an accounting ceiling over the large buffers Chuvadi allocates during one operation (decoded streams, decoded images, output pixel buffers), not a cap on the process: small objects, parsed object graphs and caches are not counted. Choose it with headroom.

## Properties

### `MaxInputBytes`

```csharp
long? MaxInputBytes
```

Gets the largest input file accepted, in bytes. Null: no limit.

### `MaxPageCount`

```csharp
int? MaxPageCount
```

Gets the most pages a document may have. Null: no limit.

### `MaxMemoryBytes`

```csharp
long? MaxMemoryBytes
```

Gets the most bytes of large buffers (decoded streams, decoded images, output pixels) one operation may allocate in total. Null: no limit.

### `MaxDecodedStreamBytes`

```csharp
long? MaxDecodedStreamBytes
```

Gets the largest size any one stream may decode to, in bytes (guards against decompression bombs). Null: no limit.

### `MaxContentOperators`

```csharp
long? MaxContentOperators
```

Gets the most content-stream operators one operation may process, counting forms, patterns, Type 3 glyphs and annotation appearances it draws (page complexity). Null: no limit.

### `MaxOutputPixels`

```csharp
long? MaxOutputPixels
```

Gets the most pixels one output raster may have (width times height). Null: no limit.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfLimits.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfLimits.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
