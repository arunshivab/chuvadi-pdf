# PdfCompressor

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Rewrites a PDF document to a smaller equivalent.

```csharp
public static class PdfCompressor
```

## Remarks

Three independent reductions are applied. First, a reachability pass from the trailer drops every object the document no longer references — orphans left behind by incremental updates, deleted pages, or earlier edits — and renumbers the survivors densely. Second, streams stored without any filter are Flate-compressed when that makes them smaller. Third, optionally, photographic images are re-encoded as JPEG.  

 The catalog graph (outlines, forms, named destinations, metadata) is preserved; this is a rewrite, not a page extraction. Because a full rewrite invalidates digital signatures and emits decrypted content, signed and encrypted documents are skipped by default — nothing is written and the returned `CompressionResult.SkipReason` says why (see `CompressionOptions.AllowSignedRewrite` and `CompressionOptions.AllowEncryptedRewrite` to override). Object streams and cross-reference streams are a recorded follow-up (the writer currently emits classic cross-reference tables).

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
