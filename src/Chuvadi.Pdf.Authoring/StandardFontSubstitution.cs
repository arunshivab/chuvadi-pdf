// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard 14 fonts), §9.6.3 (TrueType
//        fonts), §9.6.6 (character encoding); ISO 19005-1 §6.3.4 (font
//        embedding)
// PHASE: 3.33.0 (M1, D-302) — the shared standard-14 embedding engine, used by
//        PdfAWriter, PdfDocumentBuilder.EmbedStandardFonts and the
//        StandardFontEmbedder operation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// How a substitute is written for a font whose encoding is not WinAnsi
/// (3.33.0, ticket 46, D-314). WinAnsi fonts are always written as before.
/// </summary>
internal enum NonWinAnsiForm
{
    /// <summary>
    /// A non-symbolic TrueType font with a <c>/Differences</c> encoding over
    /// WinAnsiEncoding, naming each glyph with an Adobe Glyph List name, and a
    /// (3,1) Unicode cmap. PDF/A-2b and the embed operation.
    /// </summary>
    Differences = 0,

    /// <summary>
    /// A symbolic TrueType font with no <c>/Encoding</c>: one (3,0) cmap maps
    /// each character code to its glyph, and a <c>/ToUnicode</c> map gives
    /// the text. PDF/A-1b, which allows no <c>/Differences</c> in a
    /// non-symbolic TrueType font (ISO 19005-1 §6.3.7).
    /// </summary>
    Symbolic = 1,
}

/// <summary>
/// Replaces non-embedded standard 14 simple fonts (and the common Arial, Times
/// New Roman and Courier New aliases) with embedded, subsetted Liberation
/// substitutes. One session embeds each substitute once and shares it.
/// </summary>
internal sealed class StandardFontSubstitution
{
    private static readonly PdfName FontKey = PdfName.Intern("Font");
    private static readonly PdfName XObjectKey = PdfName.Intern("XObject");
    private static readonly PdfName ResourcesKey = PdfName.Intern("Resources");
    private static readonly PdfName SubtypeKey = PdfName.Intern("Subtype");
    private static readonly PdfName BaseFontKey = PdfName.Intern("BaseFont");
    private static readonly PdfName FontDescriptorKey = PdfName.Intern("FontDescriptor");
    private static readonly PdfName DescendantFontsKey = PdfName.Intern("DescendantFonts");

    private readonly PdfObjectStore _store;
    private readonly Func<PdfObjectId> _allocate;
    private readonly Func<string, byte[]?> _faces;
    private readonly Dictionary<string, PdfObjectId> _cache = new Dictionary<string, PdfObjectId>(StringComparer.Ordinal);
    private readonly HashSet<PdfDictionary> _visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _embedded = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _embeddedOrder = new List<string>();

    /// <summary>Creates a session that adds the new objects to <paramref name="store"/>.</summary>
    /// <param name="store">The object store to resolve from and add the new objects to.</param>
    /// <param name="allocate">Allocates a fresh object id.</param>
    /// <param name="faces">Returns the TTF bytes for a Liberation face key, or null.</param>
    internal StandardFontSubstitution(PdfObjectStore store, Func<PdfObjectId> allocate, Func<string, byte[]?>? faces = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(allocate);
        _store = store;
        _allocate = allocate;
        _faces = faces ?? LiberationFaces.Get;
    }

    /// <summary>Gets or sets a value indicating whether aliases (Arial, Times New Roman, Courier New) are embedded too. Default true.</summary>
    internal bool IncludeAliases { get; init; } = true;

    /// <summary>Gets or sets how fonts whose encoding is not WinAnsi are written. Default <see cref="NonWinAnsiForm.Differences"/>.</summary>
    internal NonWinAnsiForm NonWinAnsiForm { get; init; } = NonWinAnsiForm.Differences;

    /// <summary>Gets the fonts that could not be embedded, one message each.</summary>
    internal List<string> NotEmbedded { get; } = new List<string>();

    /// <summary>Gets the /BaseFont names that were embedded, in the order first seen.</summary>
    internal IReadOnlyList<string> Embedded => _embeddedOrder;

    /// <summary>
    /// Replaces the fonts of <paramref name="resources"/> and of the form
    /// XObjects it uses (recursively). Each resources dictionary is processed
    /// once per session.
    /// </summary>
    /// <param name="resources">A resources dictionary.</param>
    internal void ProcessResources(PdfDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!_visited.Add(resources))
        {
            return;
        }

        if (resources.TryGetValue(FontKey, out PdfPrimitive? fontsRef)
            && _store.Resolve(fontsRef) is PdfDictionary fonts)
        {
            foreach (PdfName key in new List<PdfName>(fonts.Keys))
            {
                if (fonts.TryGetValue(key, out PdfPrimitive? value)
                    && _store.Resolve(value) is PdfDictionary fontDict
                    && Substitute(fontDict) is PdfObjectId id)
                {
                    fonts.Set(key, new PdfReference(id));
                }
            }
        }

        if (resources.TryGetValue(XObjectKey, out PdfPrimitive? xobjRef)
            && _store.Resolve(xobjRef) is PdfDictionary xobjects)
        {
            foreach (PdfPrimitive value in new List<PdfPrimitive>(xobjects.Values))
            {
                if (_store.Resolve(value) is PdfStream form
                    && form.Dictionary.TryGetValue(ResourcesKey, out PdfPrimitive? nestedRef)
                    && _store.Resolve(nestedRef) is PdfDictionary nested)
                {
                    ProcessResources(nested);
                }
            }
        }
    }

    /// <summary>
    /// Returns the id of an embedded substitute for <paramref name="fontDict"/>,
    /// building it on first use, or null when the font is embedded already or
    /// cannot be substituted (the reason is added to <see cref="NotEmbedded"/>).
    /// </summary>
    /// <param name="fontDict">A font dictionary.</param>
    /// <returns>The substitute's object id, or null.</returns>
    internal PdfObjectId? Substitute(PdfDictionary fontDict)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        if (IsEmbedded(fontDict))
        {
            return null;
        }

        string? baseFont = NameValue(fontDict, BaseFontKey);
        if (baseFont is null)
        {
            NotEmbedded.Add("A font has no /BaseFont and cannot be embedded.");
            return null;
        }

        string? subtype = NameValue(fontDict, SubtypeKey);
        if (subtype == "Type0")
        {
            NotEmbedded.Add($"Non-embedded composite (Type0) font '{baseFont}' cannot be auto-embedded.");
            return null;
        }

        LiberationSubstitute? substitute = Standard14Map.Lookup(baseFont);
        if (substitute is null || (!IncludeAliases && !Standard14Map.IsStandard14Name(baseFont)))
        {
            NotEmbedded.Add($"Non-embedded font '{baseFont}' has no Standard-14 substitute.");
            return null;
        }

        // 3.33.0 (ticket 46): the substitute keeps the glyphs the original
        // font's encoding names. A WinAnsi font is written exactly as before.
        OriginalEncoding encoding = OriginalEncoding.Read(fontDict, _store);
        if (!encoding.IsWinAnsi && !encoding.AnyMapped)
        {
            NotEmbedded.Add($"Non-embedded font '{baseFont}' has no characters in its encoding.");
            return null;
        }

        string key = encoding.IsWinAnsi
            ? substitute.Face
            : substitute.Face + "|" + NonWinAnsiForm.ToString() + "|" + encoding.Signature;
        if (!_cache.TryGetValue(key, out PdfObjectId fontId))
        {
            byte[]? ttf = _faces(substitute.Face);
            if (ttf is null)
            {
                NotEmbedded.Add($"Substitute face '{substitute.Face}' is unavailable for '{baseFont}'.");
                return null;
            }

            fontId = encoding.IsWinAnsi
                ? BuildWinAnsi(ttf, substitute)
                : NonWinAnsiForm == NonWinAnsiForm.Symbolic
                    ? BuildSymbolic(ttf, substitute, encoding)
                    : BuildDifferences(ttf, substitute, encoding);
            _cache[key] = fontId;
        }

        if (_embedded.Add(baseFont))
        {
            _embeddedOrder.Add(baseFont);
        }

        return fontId;
    }

    /// <summary>
    /// Returns the id of the embedded WinAnsi-encoded substitute for a
    /// standard 14 base font name, building it on first use, or null when the
    /// name has no substitute (Symbol, ZapfDingbats, an unknown font).
    /// </summary>
    /// <param name="baseFont">A standard 14 base font name.</param>
    /// <returns>The substitute's object id, or null.</returns>
    internal PdfObjectId? SubstituteWinAnsi(string baseFont)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        LiberationSubstitute? substitute = Standard14Map.Lookup(baseFont);
        if (substitute is null)
        {
            return null;
        }

        if (!_cache.TryGetValue(substitute.Face, out PdfObjectId fontId))
        {
            byte[]? ttf = _faces(substitute.Face);
            if (ttf is null)
            {
                return null;
            }

            fontId = BuildWinAnsi(ttf, substitute);
            _cache[substitute.Face] = fontId;
        }

        if (_embedded.Add(baseFont))
        {
            _embeddedOrder.Add(baseFont);
        }

        return fontId;
    }

    // The substitute every release up to 3.32 wrote: a TrueType font with
    // /Encoding /WinAnsiEncoding covering the whole WinAnsi repertoire.
    private PdfObjectId BuildWinAnsi(byte[] ttf, LiberationSubstitute substitute)
    {
        EmbeddableFont program = SimpleFontProgram.Build(ttf, WinAnsiEncoding.CodeToUnicode());
        return Register(
            SimpleFontEmbedder.BuildSimpleTrueTypeFont(ttf, program, substitute.Face, substitute.Serif, _allocate));
    }

    // A non-symbolic TrueType font: /Differences over WinAnsiEncoding for
    // every code whose glyph differs from WinAnsi's, each named from the AGL,
    // and a (3,1) cmap holding the glyphs those names reach. A code the
    // original leaves unused but WinAnsi uses is not listed: PDF/A-2 allows
    // only AGL names, so it keeps WinAnsi's name and its glyph is left out.
    private PdfObjectId BuildDifferences(byte[] ttf, LiberationSubstitute substitute, OriginalEncoding encoding)
    {
        int[] winAnsi = WinAnsiEncoding.CodeToUnicode();
        int[] unicodeByCode = new int[256];
        PdfArray differences = new PdfArray();
        int lastListed = -2;
        for (int code = 0; code < 256; code++)
        {
            int unicode = encoding.UnicodeByCode[code];
            if (unicode == winAnsi[code])
            {
                unicodeByCode[code] = unicode;
                continue;
            }

            string? name = unicode > 0 ? encoding.GlyphName(code) : null;
            if (name is null)
            {
                continue;
            }

            unicodeByCode[code] = unicode;
            if (code != lastListed + 1)
            {
                differences.Add(new PdfInteger(code));
            }

            differences.Add(PdfName.Intern(name));
            lastListed = code;
        }

        PdfDictionary encodingDict = new PdfDictionary();
        encodingDict.Set(PdfName.Type, PdfName.Intern("Encoding"));
        encodingDict.Set(PdfName.Intern("BaseEncoding"), PdfName.Intern("WinAnsiEncoding"));
        encodingDict.Set(PdfName.Intern("Differences"), differences);

        EmbeddableFont program = SimpleFontProgram.Build(ttf, unicodeByCode, symbolic: false);
        (int first, int last) = CodeRange(unicodeByCode);
        return Register(SimpleFontEmbedder.BuildSimpleTrueTypeFont(
            ttf, program, substitute.Face, substitute.Serif, _allocate,
            encodingDict, symbolic: false, first, last, toUnicode: null));
    }

    // A symbolic TrueType font with no /Encoding: the (3,0) cmap maps each
    // code to the glyph the original encoding named, and /ToUnicode gives the
    // text.
    private PdfObjectId BuildSymbolic(byte[] ttf, LiberationSubstitute substitute, OriginalEncoding encoding)
    {
        int[] unicodeByCode = encoding.UnicodeByCode;
        EmbeddableFont program = SimpleFontProgram.Build(ttf, unicodeByCode, symbolic: true);
        (int first, int last) = CodeRange(unicodeByCode);
        return Register(SimpleFontEmbedder.BuildSimpleTrueTypeFont(
            ttf, program, substitute.Face, substitute.Serif, _allocate,
            encoding: null, symbolic: true, first, last, ToUnicodeCMap(unicodeByCode)));
    }

    private PdfObjectId Register(SimpleFontEmbedder.FontObjects built)
    {
        foreach (PdfIndirectObject obj in built.Objects)
        {
            _store.Add(obj);
        }

        return built.FontId;
    }

    // The first and last codes that have a character.
    private static (int First, int Last) CodeRange(int[] unicodeByCode)
    {
        int first = 0;
        while (first < 255 && unicodeByCode[first] <= 0)
        {
            first++;
        }

        int last = 255;
        while (last > first && unicodeByCode[last] <= 0)
        {
            last--;
        }

        return (first, last);
    }

    // A one-byte ToUnicode CMap for the codes that show a character (PDF
    // 32000-1:2008 §9.10.3).
    private static byte[] ToUnicodeCMap(int[] unicodeByCode)
    {
        List<string> entries = new List<string>();
        for (int code = 0; code < 256; code++)
        {
            int unicode = unicodeByCode[code];
            // A code whose glyph the substitute face lacks (as the original
            // standard font lacked it, for example the ff ligature) keeps its
            // text; its glyph is missing, as in the original.
            if (unicode > 0 && unicode <= 0xFFFF)
            {
                entries.Add(string.Format(CultureInfo.InvariantCulture, "<{0:X2}> <{1:X4}>", code, unicode));
            }
        }

        StringBuilder cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        for (int i = 0; i < entries.Count; i += 100)
        {
            int count = Math.Min(100, entries.Count - i);
            cmap.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            for (int k = i; k < i + count; k++)
            {
                cmap.Append(entries[k]).Append('\n');
            }

            cmap.Append("endbfchar\n");
        }

        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(cmap.ToString());
    }

    private bool IsEmbedded(PdfDictionary fontDict)
    {
        if (HasFontFile(fontDict))
        {
            return true;
        }

        // Type0: the descriptor lives on the descendant CIDFont.
        if (fontDict.TryGetValue(DescendantFontsKey, out PdfPrimitive? descRef)
            && _store.Resolve(descRef) is PdfArray descendants
            && descendants.Count > 0
            && _store.Resolve(descendants[0]) is PdfDictionary cidFont)
        {
            return HasFontFile(cidFont);
        }

        return false;
    }

    private bool HasFontFile(PdfDictionary fontDict)
    {
        if (!fontDict.TryGetValue(FontDescriptorKey, out PdfPrimitive? descRef)
            || _store.Resolve(descRef) is not PdfDictionary descriptor)
        {
            return false;
        }

        return descriptor.ContainsKey(PdfName.Intern("FontFile"))
            || descriptor.ContainsKey(PdfName.Intern("FontFile2"))
            || descriptor.ContainsKey(PdfName.Intern("FontFile3"));
    }

    private string? NameValue(PdfDictionary dict, PdfName key)
    {
        if (dict.TryGetValue(key, out PdfPrimitive? value) && _store.Resolve(value) is PdfName name)
        {
            return name.Value;
        }

        return null;
    }
}

/// <summary>
/// What a non-embedded simple font's encoding shows (3.33.0, ticket 46): the
/// Unicode value of each code, read exactly as text extraction reads it
/// (<c>PdfFont.SimpleEncoding</c>: a standard 14 font with no /Encoding uses
/// StandardEncoding, /Differences apply over the base encoding), and the
/// glyph names a /Differences array gave.
/// </summary>
internal sealed class OriginalEncoding
{
    private static readonly PdfName EncodingKey = PdfName.Intern("Encoding");
    private static readonly PdfName DifferencesKey = PdfName.Intern("Differences");

    private readonly string?[] _differenceNames;

    private OriginalEncoding(int[] unicodeByCode, string?[] differenceNames, bool isWinAnsi)
    {
        UnicodeByCode = unicodeByCode;
        _differenceNames = differenceNames;
        IsWinAnsi = isWinAnsi;
    }

    /// <summary>Gets the Unicode value of each code, 0 when the code shows nothing.</summary>
    internal int[] UnicodeByCode { get; }

    /// <summary>Gets a value indicating whether the encoding shows exactly what WinAnsiEncoding shows.</summary>
    internal bool IsWinAnsi { get; }

    /// <summary>Gets a value indicating whether any code shows a character.</summary>
    internal bool AnyMapped => Array.Exists(UnicodeByCode, u => u > 0);

    /// <summary>Gets a key that is equal for two encodings that show the same thing.</summary>
    internal string Signature
    {
        get
        {
            StringBuilder key = new StringBuilder(256 * 5);
            for (int code = 0; code < 256; code++)
            {
                key.Append(UnicodeByCode[code].ToString("X", CultureInfo.InvariantCulture)).Append(',');
                key.Append(GlyphName(code)).Append(';');
            }

            return key.ToString();
        }
    }

    /// <summary>
    /// Reads the encoding of <paramref name="fontDict"/>.
    /// </summary>
    /// <param name="fontDict">A simple font dictionary.</param>
    /// <param name="store">Resolves indirect references.</param>
    /// <returns>The encoding.</returns>
    internal static OriginalEncoding Read(PdfDictionary fontDict, PdfObjectStore store)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(store);

        PdfFontEncoding encoding = PdfFont.SimpleEncoding(fontDict, store);
        PdfFontEncoding winAnsi = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        int[] unicode = new int[256];
        bool isWinAnsi = true;
        for (int code = 0; code < 256; code++)
        {
            char c = encoding.GetCharacter((byte)code);
            unicode[code] = c;
            if (c != winAnsi.GetCharacter((byte)code))
            {
                isWinAnsi = false;
            }
        }

        string?[] names = new string?[256];
        if (fontDict.TryGetValue(EncodingKey, out PdfPrimitive? entry)
            && store.Resolve(entry) is PdfDictionary dict
            && dict.TryGetValue(DifferencesKey, out PdfPrimitive? diffs)
            && store.Resolve(diffs) is PdfArray array)
        {
            int code = 0;
            for (int i = 0; i < array.Count; i++)
            {
                PdfPrimitive item = store.Resolve(array[i]);
                if (item is PdfInteger start)
                {
                    code = start.Value;
                }
                else if (item is PdfName name)
                {
                    if (code >= 0 && code < 256)
                    {
                        names[code] = name.Value;
                    }

                    code++;
                }
            }
        }

        return new OriginalEncoding(unicode, names, isWinAnsi);
    }

    /// <summary>
    /// Returns an Adobe Glyph List name for the glyph code <paramref name="code"/>
    /// shows: the original /Differences name when it names that character,
    /// otherwise the table name; null when the character has none.
    /// </summary>
    /// <param name="code">A character code.</param>
    /// <returns>The glyph name, or null when the code shows nothing or has no AGL name.</returns>
    internal string? GlyphName(int code)
    {
        int unicode = UnicodeByCode[code];
        if (unicode <= 0)
        {
            return null;
        }

        string? given = _differenceNames[code];
        if (given is not null && AdobeGlyphListName(given) == unicode)
        {
            return given;
        }

        // A character with no Adobe Glyph List name (for example the Apple
        // logo, U+F8FF, at MacRoman 0xF0) cannot be named in a /Differences
        // array that PDF/A-2 accepts, and the standard fonts have no glyph
        // for it, so it is left out.
        return LatinGlyphNames.ByUnicode.TryGetValue(unicode, out string? name) ? name : null;
    }

    private static int AdobeGlyphListName(string name) =>
        AdobeGlyphList.TryGetCodePoint(name, out int cp) ? cp : -1;
}
