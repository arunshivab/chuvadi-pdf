# Asn1UniversalTag

**Enum** in `Chuvadi.Cryptography.Asn1` (Cryptography)

Universal-class ASN.1 tag numbers as assigned by ITU-T X.680.

```csharp
public enum Asn1UniversalTag : byte
```

## Remarks

Only the tags Chuvadi expects to encounter when parsing or producing cryptographic structures are enumerated. Numeric values are the tag numbers themselves, not full identifier octets.

## Values

| Name | Description |
|---|---|
| `Boolean` | BOOLEAN. X.680 §17. |
| `Integer` | INTEGER. X.680 §18. |
| `BitString` | BIT STRING. X.680 §22. |
| `OctetString` | OCTET STRING. X.680 §23. |
| `Null` | NULL. X.680 §24. |
| `ObjectIdentifier` | OBJECT IDENTIFIER. X.680 §32. |
| `Utf8String` | UTF8String. X.680 §41. |
| `Sequence` | SEQUENCE and SEQUENCE OF. X.680 §27. |
| `Set` | SET and SET OF. X.680 §28. |
| `PrintableString` | PrintableString. X.680 §41. |
| `T61String` | T61String / TeletexString. X.680 §41. |
| `IA5String` | IA5String. X.680 §41. |
| `UtcTime` | UTCTime. X.680 §47. |
| `GeneralizedTime` | GeneralizedTime. X.680 §46. |
| `BmpString` | BMPString. X.680 §41. |

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1UniversalTag.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1UniversalTag.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
