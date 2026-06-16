# RepairReport

**Class** in `Chuvadi.Pdf.IO` (IO)

Describes what `PdfRepairer` did while reconstructing a damaged PDF. Repair is best-effort: it always produces the cleanest file it can and records here what was recovered, what was rebuilt, and any content that could not be salvaged, rather than throwing on damaged input.

```csharp
public sealed class RepairReport
```

## Properties

### `Repaired`

```csharp
bool Repaired
```

True when reconstruction completed and an output file was written. False only when the input was too damaged to recover any usable structure.

### `ObjectsRecovered`

```csharp
int ObjectsRecovered
```

Number of indirect objects recovered by scanning the raw bytes.

### `ObjectsFromObjectStreams`

```csharp
int ObjectsFromObjectStreams
```

Objects recovered from inside compressed object streams (/ObjStm).

### `DuplicateObjectsResolved`

```csharp
int DuplicateObjectsResolved
```

Objects that were defined more than once (e.g. across incremental updates); the latest definition was kept and the earlier ones discarded.

### `TrailerReconstructed`

```csharp
bool TrailerReconstructed
```

True when a fresh trailer was built because the original was missing or unusable.

### `RootRecovered`

```csharp
bool RootRecovered
```

True when the document catalog (/Root) had to be located by scanning.

### `CatalogFound`

```csharp
bool CatalogFound
```

True when the document catalog (/Type /Catalog) was found.

### `HeaderRelocated`

```csharp
bool HeaderRelocated
```

True when the %PDF- header was not at offset 0 (leading junk was skipped).

### `TruncationDetected`

```csharp
bool TruncationDetected
```

True when the input appeared truncated (trailing content was missing).

### `OriginalByteCount`

```csharp
long OriginalByteCount
```

Size in bytes of the original input.

### `OutputByteCount`

```csharp
long OutputByteCount
```

Size in bytes of the repaired output.

### `Warnings`

```csharp
IReadOnlyList<string> Warnings
```

Human-readable notes about damage encountered and content that could not be recovered. Empty when the repair was clean.

---

_Source: [`src/Chuvadi.Pdf.IO/RepairReport.cs`](../../../src/Chuvadi.Pdf.IO/RepairReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
