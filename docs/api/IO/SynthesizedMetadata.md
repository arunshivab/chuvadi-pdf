# SynthesizedMetadata

**Enum** in `Chuvadi.Pdf.IO` (IO)

```csharp
public enum SynthesizedMetadata
```

## Values

| Name | Description |
|---|---|
| `None` | Synthesise neither the information dictionary nor the XMP packet. |
| `Info` | Synthesise a generic document information dictionary (/Info) when absent. |
| `Metadata` | Synthesise an XMP metadata packet (/Metadata on the catalog) when absent. |
| `All` | Synthesise both the information dictionary and the XMP packet. This is the default. |

---

_Source: [`src/Chuvadi.Pdf.IO/SynthesizedMetadata.cs`](../../../src/Chuvadi.Pdf.IO/SynthesizedMetadata.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
