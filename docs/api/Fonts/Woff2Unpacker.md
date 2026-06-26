# Woff2Unpacker

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Decodes a WOFF2 font into an sfnt (TrueType) byte array. The inverse of `Woff2Packer`; intended for converting WOFF2 assets into a form that can be embedded in a PDF.

```csharp
public static class Woff2Unpacker
```

## Methods

### `Unpack`

__static__

```csharp
static byte[] Unpack(byte[] woff2)
```

Decodes a WOFF2 font into an sfnt (TrueType) byte array.

**Parameters**

- `woff2` — The WOFF2 font bytes.

**Returns:** The decoded sfnt (TrueType) font bytes. <exception cref="ArgumentNullException">`woff2` is null.</exception> <exception cref="InvalidDataException">The input is not a supported WOFF2 font.</exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Woff2/Woff2Unpacker.cs`](../../../src/Chuvadi.Pdf.Fonts.Woff2/Woff2Unpacker.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
