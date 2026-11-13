# PatternSets

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Curated groups of `PatternRule` with checksum validators already attached, for common redaction scenarios. Each property returns a fresh list the caller may freely modify.

```csharp
public static class PatternSets
```

## Remarks

These are conservative starting points; tune patterns to your corpus before relying on them in production.

## Properties

### `Financial`

__static__

```csharp
static IReadOnlyList<PatternRule> Financial => new List<PatternRule>
```

Financial identifiers: IBAN (mod-97), ABA routing (weighted), payment card (Luhn), SWIFT/BIC, EIN, and ITIN.

### `Medical`

__static__

```csharp
static IReadOnlyList<PatternRule> Medical => new List<PatternRule>
```

Medical identifiers: NPI (Luhn over the 80840 prefix) and ICD-10 code prefixes.

### `GeneralPii`

__static__

```csharp
static IReadOnlyList<PatternRule> GeneralPii => new List<PatternRule>
```

General personally-identifiable information: US SSN, email, US phone, India PAN, India Aadhaar (Verhoeff), IPv4, and ISO dates.

---

_Source: [`src/Chuvadi.Pdf.Redaction/PatternSets.cs`](../../../src/Chuvadi.Pdf.Redaction/PatternSets.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
