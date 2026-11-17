# PatternValidators

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Reusable checksum validators for `PatternRule` post-match predicates. Each takes the matched text (formatting characters are ignored) and returns `true` when the checksum is valid.

```csharp
public static class PatternValidators
```

## Methods

### `Luhn`

__static__

```csharp
static bool Luhn(string value)
```

Validates a number with the Luhn (mod-10) checksum, ignoring spaces and dashes. Used for payment card numbers.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the Luhn checksum is valid.

### `Verhoeff`

__static__

```csharp
static bool Verhoeff(string value)
```

Validates a number with the Verhoeff checksum, ignoring spaces. Used for Indian Aadhaar numbers (the 12-digit length is enforced by the pattern).

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the Verhoeff checksum is valid.

### `Iban`

__static__

```csharp
static bool Iban(string value)
```

Validates an IBAN with the ISO 13616 mod-97 checksum, ignoring spaces.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the IBAN checksum is valid.

### `AbaRouting`

__static__

```csharp
static bool AbaRouting(string value)
```

Validates a 9-digit US ABA routing number with its weighted (3-7-1) checksum, ignoring spaces and dashes.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the routing checksum is valid.

### `Npi`

__static__

```csharp
static bool Npi(string value)
```

Validates a 10-digit US National Provider Identifier (NPI) using the Luhn checksum over the "80840" prefix, ignoring spaces and dashes.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the NPI checksum is valid.

### `Bic`

__static__

```csharp
static bool Bic(string value)
```

Validates the shape of a SWIFT / BIC code (3.35.0): 8 or 11 characters, four letters, then an ISO 3166 country code (or XK), then letters or digits. Spaces are ignored.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the code has a valid country code.

### `Itin`

__static__

```csharp
static bool Itin(string value)
```

Validates a US Individual Taxpayer Identification Number (3.35.0): nine digits starting with 9, whose fourth and fifth digits are in 50–65, 70–88, 90–92 or 94–99 (the ranges the IRS issues). Spaces and dashes are ignored.

**Parameters**

- `value` — The candidate text.

**Returns:** `true` if the number can be an ITIN.

---

_Source: [`src/Chuvadi.Pdf.Redaction/PatternValidators.cs`](../../../src/Chuvadi.Pdf.Redaction/PatternValidators.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
