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

---

_Source: [`src/Chuvadi.Pdf.Redaction/PatternValidators.cs`](../../../src/Chuvadi.Pdf.Redaction/PatternValidators.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
