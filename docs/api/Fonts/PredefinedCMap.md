# PredefinedCMap

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

A predefined CMap (PDF 32000-1:2008 §9.7.5.2): the mapping a Type 0 font's `/Encoding` name selects, from character codes to CIDs. Covers `Identity-H` and `Identity-V` and the CJK CMaps of the Adobe-GB1, Adobe-CNS1, Adobe-Japan1 and Adobe-Korea1 collections (for example `90ms-RKSJ-H`, `UniGB-UTF16-H`, `KSCms-UHC-H`).

```csharp
public sealed class PredefinedCMap
```

## Properties

### `Name`

```csharp
string Name
```

Gets the CMap's name, for example `UniJIS-UTF16-H`.

### `IsVertical`

```csharp
bool IsVertical
```

Gets whether the CMap is for vertical writing (`/WMode 1`; names ending in `-V`).

### `IsUnicode`

```csharp
bool IsUnicode => _form != UnicodeForm.None || Name.Contains("-UCS2-", StringComparison.Ordinal)
```

Gets whether the character codes are Unicode (the `Uni…-UCS2`, `-UTF16`, `-UTF8` and `-UTF32` CMaps), so a code's text is the code itself.

## Methods

### `Get`

__static__

```csharp
static PredefinedCMap? Get(string name)
```

Returns the predefined CMap with the given name, or null when there is none by that name.

**Parameters**

- `name` — The CMap name (the `/Encoding` name of a Type 0 font).

**Returns:** The CMap, or null. <exception cref="ArgumentNullException">`name` is null.</exception>

### `Next`

```csharp
int Next(ReadOnlySpan<byte> bytes, out int code, out int cid)
```

Reads the next character code from `bytes` (§9.7.6.2): the shortest byte sequence that falls in a codespace range. Returns the number of bytes it takes (at least 1 when `bytes` is not empty), and its CID (0, the .notdef CID, when the code is not mapped).

**Parameters**

- `bytes` — The string bytes, from the next code on.
- `code` — The code, its bytes packed big-endian.
- `cid` — The CID the code selects.

**Returns:** The number of bytes the code takes; 0 only for empty input.

### `CidToUnicode`

__static__

```csharp
static string? CidToUnicode(string ordering, int cid)
```

The text of a CID in a character collection (§9.10.2): through the collection's Unicode CMaps (for Adobe-Japan1, UniJIS-UTF16-H first, then the half-width, vertical and later ones), read backwards. Returns null for a CID with no Unicode value, or an ordering other than GB1, CNS1, Japan1 and Korea1.

**Parameters**

- `ordering` — The `/CIDSystemInfo` `/Ordering`, for example "Japan1".
- `cid` — The CID.

**Returns:** The text, or null. <exception cref="ArgumentNullException">`ordering` is null.</exception>

### `CodeToUnicode`

```csharp
string? CodeToUnicode(ReadOnlySpan<byte> codeBytes)
```

The text of a code of a Unicode CMap (see `IsUnicode`), or null for any other CMap or bytes that are not valid in its form.

**Parameters**

- `codeBytes` — The code's bytes, as `Next` took them.

**Returns:** The text, or null.

---

_Source: [`src/Chuvadi.Pdf.Fonts/PredefinedCMap.cs`](../../../src/Chuvadi.Pdf.Fonts/PredefinedCMap.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
