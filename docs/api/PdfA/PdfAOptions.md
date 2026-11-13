# PdfAOptions

**Class** in `Chuvadi.Pdf.PdfA` (PdfA)

Options controlling PDF/A production.

```csharp
public sealed class PdfAOptions
```

## Properties

### `Conformance`

```csharp
required PdfAConformance Conformance
```

The conformance level to target.

### `OutputIntentIccProfile`

```csharp
byte[]? OutputIntentIccProfile
```

An ICC RGB output profile for the output intent. When null, a bundled public-domain sRGB profile is used.

### `OutputConditionIdentifier`

```csharp
string OutputConditionIdentifier
```

The output condition identifier recorded in the output intent.

### `RegistryName`

```csharp
string? RegistryName
```

An optional registry name for the output intent.

### `Title`

```csharp
string? Title
```

An optional document title written to the XMP metadata.

### `Author`

```csharp
string? Author
```

An optional document author written to the XMP metadata.

### `AssumeUtcForZonelessDates`

```csharp
bool AssumeUtcForZonelessDates
```

Gets whether an `/Info` date that gives no time zone is written to the XMP metadata as UTC (3.34.0). By default (false) such a date is left out of the XMP, since it has no known relation to UT (PDF 32000-1:2008 §7.9.4) and validators read an XMP date without a zone in their own local zone; either way `PdfAResult.Warnings` says so. Omitted parts take the §7.9.4 defaults (month and day 01, the rest zero).

---

_Source: [`src/Chuvadi.Pdf.PdfA/PdfAOptions.cs`](../../../src/Chuvadi.Pdf.PdfA/PdfAOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
