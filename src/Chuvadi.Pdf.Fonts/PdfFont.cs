// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6 — Simple fonts
//        PDF 32000-1:2008 §9.7 — Composite fonts (Type0)
//        PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
//        v2.1.4 — longest-match-first byte consumption against the parsed
//                 ToUnicode map. Previously, simple fonts decoded one byte
//                 at a time and composite fonts decoded two bytes; that
//                 misses ToUnicode entries whose source code is wider
//                 than the font's nominal width — notably Word's
//                 Wingdings export, which encodes the original 0xFC
//                 glyph byte as the three UTF-8 bytes E2 9C 93 paired
//                 with a CMap entry <E29C93> <2713>. The new decoder
//                 derives the maximum window size from the CMap's
//                 codespace ranges and the highest key in the mapping,
//                 then tries widths from longest to shortest at each
//                 byte position. Encoding-based fallback for single
//                 bytes is preserved.
//        v2.1.5 — exposes ToUnicodeMap as a public read-only property so
//                 consumers (notably FontEmbedder when augmenting an
//                 embedded TrueType font's cmap) can inspect the
//                 CID → Unicode mapping that drives glyph-side remapping.
// Maps character codes from PDF text strings to Unicode codepoints.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// Represents a PDF font and provides character code to Unicode mapping
/// for text extraction purposes.
/// </summary>
/// <remarks>
/// Phase 1 supports text extraction only — no glyph rendering or metrics.
///
/// The mapping strategy, in priority order:
/// <list type="number">
///   <item>ToUnicode CMap — present in most modern PDFs, most accurate.</item>
///   <item>Encoding + glyph name lookup — for simple fonts without ToUnicode.</item>
///   <item>Direct code-as-Unicode — last resort for unrecognised configurations.</item>
/// </list>
///
/// PDF 32000-1:2008 §9.10.2 — Mapping character codes to Unicode values.
/// </remarks>
public sealed class PdfFont
{
    private readonly IReadOnlyDictionary<int, string>? _toUnicodeMap;
    private readonly PdfFontEncoding? _encoding;
    private readonly int _maxByteCount;

    // PDF codes are conventionally up to 4 bytes; longer windows would
    // overflow a signed 32-bit code anyway. The fallback (when neither a
    // codespace range nor the mapping suggests a wider window) is 2 bytes
    // for composite fonts and 1 byte for simple fonts — matching pre-v2.1.4
    // behaviour for the common cases.
    private const int MaxSupportedByteCount = 4;

    private PdfFont(
        IReadOnlyDictionary<int, string>? toUnicodeMap,
        IReadOnlyList<CodespaceRange>? codespaceRanges,
        PdfFontEncoding? encoding,
        bool isComposite)
    {
        _toUnicodeMap = toUnicodeMap;
        _encoding = encoding;
        _maxByteCount = DeriveMaxByteCount(toUnicodeMap, codespaceRanges, isComposite);
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PdfFont"/> from a font dictionary.
    /// </summary>
    /// <param name="fontDict">The font dictionary from the page Resources.</param>
    /// <param name="resolver">Used to resolve indirect objects (e.g. ToUnicode stream).</param>
    public static PdfFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        if (fontDict is null)
        {
            throw new ArgumentNullException(nameof(fontDict));
        }

        if (resolver is null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        PdfName? subtype = fontDict.Subtype;
        bool isComposite = subtype is not null &&
            subtype.Value.Equals("Type0", StringComparison.Ordinal);

        // Try ToUnicode first — most reliable for all font types.
        CMapParseResult? toUnicode = ParseToUnicode(fontDict, resolver);

        // For simple fonts, also parse the encoding as fallback.
        PdfFontEncoding? encoding = null;

        if (!isComposite)
        {
            PdfPrimitive? encodingEntry =
                fontDict.TryGetValue(PdfName.Intern("Encoding"), out PdfPrimitive? enc)
                    ? enc
                    : null;

            if (encodingEntry is PdfReference encRef)
            {
                encodingEntry = resolver.Resolve(encRef);
            }

            encoding = PdfFontEncoding.Build(encodingEntry);
        }

        return new PdfFont(
            toUnicode?.Mapping,
            toUnicode?.CodespaceRanges,
            encoding,
            isComposite);
    }

    /// <summary>
    /// Returns a default font that maps codes directly to their Latin-1 equivalents.
    /// Used when no font dictionary is available.
    /// </summary>
    public static PdfFont Default()
    {
        return new PdfFont(
            toUnicodeMap: null,
            codespaceRanges: null,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"),
            isComposite: false);
    }

    /// <summary>
    /// Constructs a font directly from explicit mappings, bypassing PDF
    /// dictionary parsing. Useful for synthetic PDFs, programmatic font
    /// construction, and unit tests that need deterministic mapping state.
    /// </summary>
    /// <param name="toUnicodeMap">
    /// Source-code-to-Unicode mapping. Keys are 1- to 4-byte codes packed
    /// big-endian into an int (e.g. the byte sequence 0xE2 0x9C 0x93 is the
    /// key 0xE29C93). May be <c>null</c>.
    /// </param>
    /// <param name="codespaceRanges">
    /// Optional codespace declarations from the source CMap. Used (together
    /// with the maximum key in <paramref name="toUnicodeMap"/>) to bound the
    /// byte window the decoder considers. May be <c>null</c>.
    /// </param>
    /// <param name="encoding">
    /// Optional single-byte encoding for fallback when no
    /// <paramref name="toUnicodeMap"/> entry matches a position.
    /// </param>
    /// <param name="isComposite">
    /// Whether this represents a Type0/composite font. Affects the default
    /// byte-window width when neither codespace ranges nor mapping keys
    /// suggest a wider window.
    /// </param>
    public static PdfFont FromMappings(
        IReadOnlyDictionary<int, string>? toUnicodeMap,
        IReadOnlyList<CodespaceRange>? codespaceRanges = null,
        PdfFontEncoding? encoding = null,
        bool isComposite = false)
    {
        return new PdfFont(toUnicodeMap, codespaceRanges, encoding, isComposite);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// The ToUnicode CMap mapping (source code → Unicode string) for this
    /// font, or <c>null</c> when the font has no ToUnicode entry.
    /// </summary>
    /// <remarks>
    /// Keys are character codes (1- to 4-byte source codes packed
    /// big-endian into an integer); values are the corresponding Unicode
    /// strings, which may be one or more <c>char</c> code units. For most
    /// font dictionaries the keys equal the glyph indices (CIDs) used in
    /// text-showing operators; this is the basis for the v2.1.5 cmap
    /// remap path in <c>FontEmbedder</c>.
    /// </remarks>
    public IReadOnlyDictionary<int, string>? ToUnicodeMap => _toUnicodeMap;

    /// <summary>
    /// Converts a sequence of bytes from a PDF text string operator to Unicode text.
    /// </summary>
    /// <param name="bytes">The raw bytes from a Tj, TJ, or similar operator.</param>
    /// <returns>The decoded Unicode string.</returns>
    /// <remarks>
    /// v2.1.4: at each byte position the decoder tries widths from
    /// <see cref="MaxSupportedByteCount"/> down to 1 against the ToUnicode
    /// mapping; the first width whose packed code is present in the map
    /// consumes that many bytes. If no width matches, the byte falls
    /// through to the single-byte encoding fallback and the cursor advances
    /// by one. This correctly handles 1-byte simple-font CMaps, 2-byte
    /// composite CMaps, and 3-byte CMaps emitted by Word for
    /// non-Latin/symbol fonts (UTF-8 encoding of the semantic Unicode
    /// codepoint as the source code).
    /// </remarks>
    public string Decode(byte[] bytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(bytes.Length);

        int i = 0;

        while (i < bytes.Length)
        {
            int matched = TryMatchLongest(bytes, i, sb);

            if (matched > 0)
            {
                i += matched;
            }
            else
            {
                // No multi-byte hit; fall through to the single-byte
                // encoding/fallback decode and advance one byte.
                sb.Append(DecodeCode(bytes[i]));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a single character code to a Unicode string.
    /// Returns an empty string when the code cannot be mapped.
    /// </summary>
    public string DecodeCode(int code)
    {
        // ToUnicode CMap takes priority.
        if (_toUnicodeMap is not null && _toUnicodeMap.TryGetValue(code, out string? unicode))
        {
            return unicode;
        }

        // Encoding fallback for simple fonts.
        if (_encoding is not null && code >= 0 && code <= 255)
        {
            char c = _encoding.GetCharacter((byte)code);

            if (c != '\0')
            {
                return c.ToString();
            }
        }

        // Last resort: direct code-point interpretation.
        if (code >= 0x20 && code <= 0x7E)
        {
            return ((char)code).ToString();
        }

        return string.Empty;
    }

    // ── Private decoding ──────────────────────────────────────────────────

    /// <summary>
    /// Attempts to match the longest ToUnicode entry starting at
    /// <paramref name="start"/>. Appends the matched Unicode string to
    /// <paramref name="sb"/> and returns the number of bytes consumed,
    /// or 0 if no width matches.
    /// </summary>
    private int TryMatchLongest(byte[] bytes, int start, StringBuilder sb)
    {
        if (_toUnicodeMap is null)
        {
            return 0;
        }

        int available = bytes.Length - start;
        int tryCount = _maxByteCount < available ? _maxByteCount : available;

        for (int n = tryCount; n >= 1; n--)
        {
            int code = 0;

            for (int k = 0; k < n; k++)
            {
                code = (code << 8) | bytes[start + k];
            }

            if (_toUnicodeMap.TryGetValue(code, out string? unicode))
            {
                sb.Append(unicode);
                return n;
            }
        }

        return 0;
    }

    /// <summary>
    /// Computes the maximum byte width the decoder should consider when
    /// matching ToUnicode entries. The result is the maximum of: the
    /// largest <see cref="CodespaceRange.ByteCount"/>, the byte width
    /// implied by the highest key in <paramref name="map"/>, and the
    /// minimum implied by the font subtype (2 for composite, 1 for
    /// simple). Capped at <see cref="MaxSupportedByteCount"/>.
    /// </summary>
    private static int DeriveMaxByteCount(
        IReadOnlyDictionary<int, string>? map,
        IReadOnlyList<CodespaceRange>? ranges,
        bool isComposite)
    {
        int maxFromRanges = 0;

        if (ranges is not null)
        {
            foreach (CodespaceRange r in ranges)
            {
                if (r.ByteCount > maxFromRanges)
                {
                    maxFromRanges = r.ByteCount;
                }
            }
        }

        int maxFromKeys = 0;

        if (map is not null)
        {
            int maxKey = 0;

            foreach (int key in map.Keys)
            {
                if (key > maxKey)
                {
                    maxKey = key;
                }
            }

            if (maxKey > 0xFFFFFF) { maxFromKeys = 4; }
            else if (maxKey > 0xFFFF) { maxFromKeys = 3; }
            else if (maxKey > 0xFF) { maxFromKeys = 2; }
            else { maxFromKeys = 1; }
        }

        int floor = isComposite ? 2 : 1;
        int result = floor;

        if (maxFromRanges > result) { result = maxFromRanges; }
        if (maxFromKeys > result) { result = maxFromKeys; }

        if (result > MaxSupportedByteCount) { result = MaxSupportedByteCount; }

        return result;
    }

    // ── ToUnicode parsing ─────────────────────────────────────────────────

    private static byte[] DecodeFilterChain(PdfStream stream, IPdfObjectResolver resolver)
    {
        byte[] current = stream.RawBytes;
        PdfPrimitive? filter = stream.Dictionary.TryGetValue(PdfName.Filter, out PdfPrimitive? fv)
            ? resolver.Resolve(fv)
            : null;
        PdfPrimitive? parms = stream.Dictionary.TryGetValue(PdfName.Intern("DecodeParms"), out PdfPrimitive? pv)
            ? resolver.Resolve(pv)
            : null;
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();

        if (filter is PdfName single)
        {
            return pipeline.Decode(
                FilterRegistry.ResolveAlias(single.Value), current, FilterParameters.FromDictionary(parms, 0));
        }

        if (filter is PdfArray chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (resolver.Resolve(chain[i]) is PdfName name)
                {
                    current = pipeline.Decode(
                        FilterRegistry.ResolveAlias(name.Value), current, FilterParameters.FromDictionary(parms, i));
                }
            }
        }

        return current;
    }

    private static CMapParseResult? ParseToUnicode(
        PdfDictionary fontDict,
        IPdfObjectResolver resolver)
    {
        if (!fontDict.TryGetValue(PdfName.Intern("ToUnicode"), out PdfPrimitive? toUnicodeRef))
        {
            return null;
        }

        PdfPrimitive resolved = resolver.Resolve(toUnicodeRef);

        if (resolved is not PdfStream stream)
        {
            return null;
        }

        // Decode the stream through its whole /Filter chain (a single name or
        // an array), with the matching /DecodeParms for each filter.
        byte[] rawBytes = DecodeFilterChain(stream, resolver);

        CMapParser parser = new CMapParser(rawBytes);
        return parser.ParseFull();
    }
}
