# CommonPatterns

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Pre-built regex strings for common PHI / PII tokens.

```csharp
public static class CommonPatterns
```

## Remarks

These are conservative starting points. Real-world documents have many edge cases (whitespace inside identifiers, OCR artefacts, locale-specific formats); production deployments should tune patterns to their corpus.

## Methods

### `LabeledValue`

__static__

```csharp
static PatternRule LabeledValue(string label)
```

Builds a rule that redacts a labelled value: the given label followed by an optional separator and an alphanumeric value (e.g. "MRN: 0099123"). The whole match - label and value - is redacted. Prefer this over a bare number regex for identifiers (MRN, account, policy) that have no checksum.

**Parameters**

- `label` — The literal label text preceding the value.

**Returns:** A `PatternRule` matching the label and its value.

## Fields

### `UsSsn`

```csharp
const string UsSsn = @"\b\d{3}-\d{2}-\d{4}\b"
```

US Social Security Number. Matches the conventional XXX-XX-XXXX format.

### `UsPhone`

```csharp
const string UsPhone = @"\b(?:\(\d{3}\)\s?|\d{3}[-.])\d{3}[-.]\d{4}\b"
```

US phone number. Matches (XXX) XXX-XXXX, XXX-XXX-XXXX, and XXX.XXX.XXXX.

### `Email`

```csharp
const string Email = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"
```

Email address. RFC-5322 inspired but conservative enough to avoid false positives.

### `Icd10Prefix`

```csharp
const string Icd10Prefix = @"\b" + Icd10Category + @"(?:\.[0-9A-Z]{1,4})?\b"
```

ICD-10 code prefix. Matches the letter+two-digit prefix of any ICD-10 code, e.g. "E11" or "J45.901". Intentionally loose; tune downward if you want exact codes.

### `Icd10Code`

```csharp
const string Icd10Code = @"\b" + Icd10Category + @"\.[0-9A-Z]{1,4}\b"
```

ICD-10 code with its decimal part, e.g. "J45.901" or "E11.9" (3.35.0). A three-character category on its own ("E11") is matched only after a label, by `Icd10Labeled`.

### `Icd10Labeled`

```csharp
const string Icd10Labeled =
```

ICD-10 code, with or without its decimal part, after a label ("ICD-10", "ICD", "Dx", "diagnosis", any case, then optional ':' or '#'), e.g. the "E11" in "Dx: E11" (3.35.0). Only the code is matched.

### `CreditCard`

```csharp
const string CreditCard = @"\b(?:\d[ -]?){13,19}\b"
```

Credit card primary account number. Matches 13-19 digits possibly grouped by spaces or dashes. Does not validate the Luhn checksum — match precision is the caller's responsibility.

### `IsoDate`

```csharp
const string IsoDate = @"\b\d{4}[-/]\d{2}[-/]\d{2}\b"
```

ISO-8601 date. Matches YYYY-MM-DD and YYYY/MM/DD.

### `UsZip`

```csharp
const string UsZip = @"\b\d{5}(?:-\d{4})?\b"
```

US ZIP code. Matches 5-digit and ZIP+4 forms.

### `UkNhsNumber`

```csharp
const string UkNhsNumber = @"\b\d{3}\s?\d{3}\s?\d{4}\b"
```

UK NHS number (10 digits, optionally grouped 3-3-4 with spaces).

### `IndiaPan`

```csharp
const string IndiaPan = @"\b[A-Z]{5}[0-9]{4}[A-Z]\b"
```

Indian Permanent Account Number (PAN): five letters, four digits, one letter. Format-only; PAN has no checksum.

### `IndiaAadhaar`

```csharp
const string IndiaAadhaar = @"\b[2-9]\d{3}\s?\d{4}\s?\d{4}\b"
```

Indian Aadhaar number: 12 digits (first digit 2-9), optionally grouped 4-4-4 with spaces. Pair with `PatternValidators.Verhoeff`.

### `Iban`

```csharp
const string Iban = @"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b"
```

IBAN: two-letter country, two check digits, 11-30 alphanumerics. Pair with `PatternValidators.Iban` (mod-97).

### `AbaRouting`

```csharp
const string AbaRouting = @"\b\d{9}\b"
```

US ABA routing number: nine digits. Very loose on its own; pair with `PatternValidators.AbaRouting`.

### `Swift`

```csharp
const string Swift =
```

SWIFT / BIC code: four letters (the bank), an ISO 3166 country code, two alphanumerics (the location) and an optional three-character branch. Pair with `PatternValidators.Bic`, which checks the country code.

### `Ein`

```csharp
const string Ein = @"\b\d{2}-\d{7}\b"
```

US Employer Identification Number (EIN): XX-XXXXXXX.

### `Itin`

```csharp
const string Itin = @"\b9\d{2}-\d{2}-\d{4}\b"
```

US Individual Taxpayer Identification Number (ITIN): 9XX-XX-XXXX. Pair with `PatternValidators.Itin`, which checks the middle group (3.35.0).

### `Npi`

```csharp
const string Npi = @"\b\d{10}\b"
```

US National Provider Identifier (NPI): ten digits. Loose on its own; pair with `PatternValidators.Npi`.

### `IPv4`

```csharp
const string IPv4 =
```

IPv4 address in dotted-decimal form.

---

_Source: [`src/Chuvadi.Pdf.Redaction/CommonPatterns.cs`](../../../src/Chuvadi.Pdf.Redaction/CommonPatterns.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
