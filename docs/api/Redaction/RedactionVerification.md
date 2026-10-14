# RedactionVerification

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

The result of verifying that redaction regions are empty.

```csharp
public sealed class RedactionVerification
```

## Properties

### `Survivors`

```csharp
IReadOnlyList<RedactionSurvivor> Survivors
```

Gets everything still found inside the regions.

### `IsClean`

```csharp
bool IsClean => Survivors.Count == 0
```

Gets whether nothing survives inside any region.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
