# SynthesizedMetadata

**Enum** in `Chuvadi.Pdf.IO` (IO)

Selects which document-level metadata `PdfWriter` synthesises when it is absent from the objects being written. The file identifier (/ID) is always written, because it carries no document content and keeps output deterministic and viewer-friendly. The default is `All`, which preserves the writer's standard behaviour; pass a reduced set to suppress synthesis (for example when deliberately stripping metadata). Metadata that is already present in the written objects is never altered — these flags govern synthesis of absent entries only.

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
