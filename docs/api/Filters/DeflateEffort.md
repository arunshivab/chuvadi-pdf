# DeflateEffort

**Enum** in `Chuvadi.Pdf.Filters` (Filters)

Effort level for FlateDecode (DEFLATE) compression.

```csharp
public enum DeflateEffort
```

## Values

| Name | Description |
|---|---|
| `Default` | Fast path: a single greedy LZ77 parse emitted with whichever of the stored, fixed-Huffman, or dynamic-Huffman encodings is smallest. |
| `Maximum` | Maximum effort: in addition to the `Default` candidates, also tries the runtime (BCL) deflater and an iterated optimal-parse ("zopfli-style") encoding, keeping the smallest result. Slower, but yields the best lossless ratio. Output stays a valid zlib/DEFLATE stream. |

---

_Source: [`src/Chuvadi.Pdf.Filters/DeflateEffort.cs`](../../../src/Chuvadi.Pdf.Filters/DeflateEffort.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
