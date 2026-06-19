# CommonPatterns

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Pre-built regex strings for common PHI / PII tokens.

```csharp
public static class CommonPatterns
```

## Remarks

These are conservative starting points. Real-world documents have many edge cases (whitespace inside identifiers, OCR artefacts, locale-specific formats); production deployments should tune patterns to their corpus.

## Properties

### `@"\b\d`

```csharp
const string UsSsn = @"\b\d
```

US Social Security Number. Matches the conventional XXX-XX-XXXX format.

### `@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]`

```csharp
const string Email = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]
```

Email address. RFC-5322 inspired but conservative enough to avoid false positives.

### `@"\b\d`

```csharp
const string IsoDate = @"\b\d
```

ISO-8601 date. Matches YYYY-MM-DD and YYYY/MM/DD.

### `@"\b\d`

```csharp
const string UsZip = @"\b\d
```

US ZIP code. Matches 5-digit and ZIP+4 forms.

### `@"\b\d`

```csharp
const string UkNhsNumber = @"\b\d
```

UK NHS number (10 digits, optionally grouped 3-3-4 with spaces).

### `@"\b[A-Z]`

```csharp
const string IndiaPan = @"\b[A-Z]
```

Indian Permanent Account Number (PAN): five letters, four digits, one letter. Format-only; PAN has no checksum.

### `@"\b[2-9]\d`

```csharp
const string IndiaAadhaar = @"\b[2-9]\d
```

Indian Aadhaar number: 12 digits (first digit 2-9), optionally grouped 4-4-4 with spaces. Pair with `PatternValidators.Verhoeff`.

### `@"\b[A-Z]`

```csharp
const string Iban = @"\b[A-Z]
```

IBAN: two-letter country, two check digits, 11-30 alphanumerics. Pair with `PatternValidators.Iban` (mod-97).

### `@"\b\d`

```csharp
const string AbaRouting = @"\b\d
```

US ABA routing number: nine digits. Very loose on its own; pair with `PatternValidators.AbaRouting`.

### `@"\b[A-Z]`

```csharp
const string Swift = @"\b[A-Z]
```

SWIFT / BIC code: six letters, two alphanumerics, optional three-character branch. Format-only.

### `@"\b\d`

```csharp
const string Ein = @"\b\d
```

US Employer Identification Number (EIN): XX-XXXXXXX.

### `@"\b9\d`

```csharp
const string Itin = @"\b9\d
```

US Individual Taxpayer Identification Number (ITIN): 9XX-XX-XXXX.

### `@"\b\d`

```csharp
const string Npi = @"\b\d
```

US National Provider Identifier (NPI): ten digits. Loose on its own; pair with `PatternValidators.Npi`.

## Methods

### `@"\b`

```csharp
const string UsPhone = @"\b(?:\(\d
```

US phone number. Matches (XXX) XXX-XXXX, XXX-XXX-XXXX, and XXX.XXX.XXXX.

### `@"\b[A-TV-Z][0-9][0-9A-Z]`

```csharp
const string Icd10Prefix = @"\b[A-TV-Z][0-9][0-9A-Z](?:\.[0-9A-Z]
```

ICD-10 code prefix. Matches the letter+two-digit prefix of any ICD-10 code, e.g. "E11" or "J45.901". Intentionally loose; tune downward if you want exact codes.

### `@"\b`

```csharp
const string CreditCard = @"\b(?:\d[ -]?)
```

Credit card primary account number. Matches 13-19 digits possibly grouped by spaces or dashes. Does not validate the Luhn checksum — match precision is the caller's responsibility.

### `LabeledValue`

__static__

```csharp
static PatternRule LabeledValue(string label)
```

Builds a rule that redacts a labelled value: the given label followed by an optional separator and an alphanumeric value (e.g. "MRN: 0099123"). The whole match - label and value - is redacted. Prefer this over a bare number regex for identifiers (MRN, account, policy) that have no checksum.

**Parameters**

- `label` — The literal label text preceding the value.

**Returns:** A `PatternRule` matching the label and its value.

---

_Source: [`src/Chuvadi.Pdf.Redaction/CommonPatterns.cs`](../../../src/Chuvadi.Pdf.Redaction/CommonPatterns.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
