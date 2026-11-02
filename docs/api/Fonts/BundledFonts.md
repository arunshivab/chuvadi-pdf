# BundledFonts

**Class** in `Chuvadi.Pdf.Fonts.Bundled` (Fonts)

The fonts bundled with Chuvadi: LiPi Sans, one face per script (Latin, Tamil, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Telugu, Kannada and Malayalam), licensed under the SIL Open Font License 1.1 (the licence text is in this package).

```csharp
public static class BundledFonts
```

## Properties

### `FaceNames`

__static__

```csharp
static IReadOnlyList<string> FaceNames
```

Gets the face names, such as `LiPi-Sans-Tamil`.

## Methods

### `FaceForScript`

__static__

```csharp
static string? FaceForScript(string script)
```

Returns the face name for a script name (`"Tamil"` gives `LiPi-Sans-Tamil`), or null when no bundled face covers it.

**Parameters**

- `script` — The script name, as in `FaceNames` without the prefix.

**Returns:** The face name, or null. <exception cref="ArgumentNullException">`script` is null.</exception>

### `GetFontProgram`

__static__

```csharp
static byte[] GetFontProgram(string faceName)
```

Returns a face's TrueType program, decoded from its WOFF2 file on the first call and shared after that. The array is shared: do not change it.

**Parameters**

- `faceName` — A name from `FaceNames`.

**Returns:** The decoded TrueType font program. <exception cref="ArgumentNullException">`faceName` is null.</exception> <exception cref="ArgumentException">`faceName` is not a bundled face.</exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Bundled/BundledFonts.cs`](../../../src/Chuvadi.Pdf.Fonts.Bundled/BundledFonts.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
