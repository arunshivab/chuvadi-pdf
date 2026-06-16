# PdfRepairer

**Class** in `Chuvadi.Pdf.IO` (IO)

Repairs structurally damaged PDFs that standard readers reject — broken or missing cross-reference tables, wrong `startxref` offsets, missing or corrupt trailers, leading junk before the header, truncated files, and duplicate objects from incremental updates. The original byte offsets are ignored; every `N G obj … endobj` is located by scanning, objects inside compressed object streams (/ObjStm) and cross-reference streams are recovered, and a clean file with a freshly built classic cross-reference table is written. Repair is best-effort: it always emits the best file it can and reports what could not be salvaged via `RepairReport` rather than throwing.

```csharp
public static class PdfRepairer
```

## Methods

### `Repair`

__static__

```csharp
static RepairReport Repair(Stream input, Stream output)
```

Reconstructs `input` and writes a repaired PDF to `output`.

**Parameters**

- `input` — The damaged PDF. Read in full.
- `output` — Destination for the repaired PDF.

**Returns:** A report describing what was recovered and rebuilt.

---

_Source: [`src/Chuvadi.Pdf.IO/PdfRepairer.cs`](../../../src/Chuvadi.Pdf.IO/PdfRepairer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
