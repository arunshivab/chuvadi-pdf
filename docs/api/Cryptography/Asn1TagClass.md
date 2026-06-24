# Asn1TagClass

**Enum** in `Chuvadi.Cryptography.Asn1` (Cryptography)

ASN.1 tag class. Encoded in bits 8 and 7 of the first identifier byte per ITU-T X.690 §8.1.2.

```csharp
public enum Asn1TagClass : byte
```

## Remarks

The four classes serve distinct roles in ASN.1: `Universal` tags are reserved for the built-in types defined by X.680 (INTEGER, OCTET STRING, SEQUENCE, etc.). `Application`, `ContextSpecific`, and `Private` tags are used by individual ASN.1 specifications to disambiguate fields, with `ContextSpecific` being by far the most common in the standards Chuvadi cares about (X.509, CMS, OCSP).

## Values

| Name | Description |
|---|---|
| `Universal` | Built-in ASN.1 types defined by X.680. Bits 8-7 = 00. |
| `Application` | Application-specific tags. Bits 8-7 = 01. Rare in modern specs. |
| `ContextSpecific` | Context-specific tags, used inside SEQUENCE / CHOICE / etc. Bits 8-7 = 10. |
| `Private` | Private-use tags. Bits 8-7 = 11. Unused in standard specs. |

---

_Source: [`src/Chuvadi.Cryptography/Asn1/Asn1TagClass.cs`](../../../src/Chuvadi.Cryptography/Asn1/Asn1TagClass.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
