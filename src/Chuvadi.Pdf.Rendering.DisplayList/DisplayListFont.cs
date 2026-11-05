// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — font access on the structured display list for vector output
//        (D-108 item b, D-144): one shared object per font dictionary, with the
//        embedded program, its kind, the Type 0 details and the substitute the
//        raster renderer would use.
// SPEC:  PDF 32000-1:2008 §9.6 (simple fonts), §9.7 (Type 0 / CID fonts),
//        §9.8 (font descriptors), §9.9 (embedded font programs).

using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Walking;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>The kind of an embedded font program (§9.9, Table 124).</summary>
public enum FontProgramKind
{
    /// <summary>The font is not embedded (or is a Type 3 font, which has no program).</summary>
    None = 0,

    /// <summary>A Type 1 program (<c>/FontFile</c>).</summary>
    Type1 = 1,

    /// <summary>A TrueType program (<c>/FontFile2</c>).</summary>
    TrueType = 2,

    /// <summary>A bare CFF program for a simple font (<c>/FontFile3</c>, <c>/Subtype /Type1C</c>).</summary>
    Cff = 3,

    /// <summary>A bare CID-keyed CFF program (<c>/FontFile3</c>, <c>/Subtype /CIDFontType0C</c>).</summary>
    CidCff = 4,

    /// <summary>An OpenType program (<c>/FontFile3</c>, <c>/Subtype /OpenType</c>).</summary>
    OpenType = 5,
}

/// <summary>The embedding permission of a font program (OpenType <c>OS/2</c> <c>fsType</c>).</summary>
public enum FontEmbeddingPermission
{
    /// <summary>Not known: the program has no <c>OS/2</c> table (Type 1, bare CFF) or is not embedded.</summary>
    Unknown = 0,

    /// <summary>Installable embedding (<c>fsType</c> 0): no restriction.</summary>
    Installable = 1,

    /// <summary>Restricted licence embedding (bit 1): must not be embedded or sent to a device that keeps it.</summary>
    Restricted = 2,

    /// <summary>Preview and print embedding (bit 2): may be embedded for viewing and printing.</summary>
    PreviewAndPrint = 3,

    /// <summary>Editable embedding (bit 3): may be embedded for viewing, printing and editing.</summary>
    Editable = 4,
}

/// <summary>A font program's embedding rights (OpenType <c>OS/2</c> <c>fsType</c>).</summary>
/// <param name="Permission">The usage permission.</param>
/// <param name="NoSubsetting">Bit 8: the font must not be subset before embedding.</param>
/// <param name="BitmapOnly">Bit 9: only bitmaps contained in the font may be embedded.</param>
public readonly record struct FontEmbeddingRights(FontEmbeddingPermission Permission, bool NoSubsetting, bool BitmapOnly);

/// <summary>The <c>/CIDSystemInfo</c> of a CIDFont (§9.7.3).</summary>
/// <param name="Registry">The issuer of the character collection, for example "Adobe".</param>
/// <param name="Ordering">The character collection, for example "Identity" or "Japan1".</param>
/// <param name="Supplement">The supplement number.</param>
public sealed record CidSystemInfo(string Registry, string Ordering, int Supplement);

/// <summary>
/// A font used by text on a page, as the structured display list sees it: one
/// shared instance per font dictionary, referenced from every <see cref="TextOp"/>
/// drawn with it. Resolved through the same resources as the text (page, form,
/// annotation appearance, pattern or Type 3 glyph), so the same resource name
/// in two places gives two different objects when it names two fonts.
/// </summary>
/// <remarks>
/// It gives a vector consumer what it needs to embed or substitute the font:
/// the program bytes and their kind, the Type 0 details and, for a font the PDF
/// does not embed, the substitute the raster renderer draws it with. It does
/// not convert programs to other formats.
/// </remarks>
public sealed class DisplayListFont
{
    private readonly PdfObjectStore _objects;
    private readonly PdfStream? _programStream;
    private readonly object _gate = new object();
    private byte[]? _program;
    private bool _programRead;
    private int[]? _cidToGid;
    private bool _cidToGidRead;
    private FontRenderer? _renderer;
    private Type1FontRenderer? _type1;
    private bool _rendererRead;
    private Chuvadi.Pdf.Fonts.PdfFont? _pdfFont;
    private bool _pdfFontRead;

    private DisplayListFont(PdfDictionary dictionary, PdfObjectStore objects)
    {
        _objects = objects;
        Dictionary = dictionary;
        Subtype = NameOf(dictionary, "Subtype") ?? string.Empty;
        BaseFont = NameOf(dictionary, "BaseFont") ?? string.Empty;
        EncodingName = NameOf(dictionary, "Encoding");

        PdfDictionary target = dictionary;
        if (Subtype == "Type0")
        {
            PdfArray? descendants = objects.ResolveAs<PdfArray>(Get(dictionary, "DescendantFonts"));
            DescendantFont = descendants is { Count: > 0 } ? objects.ResolveAs<PdfDictionary>(descendants[0]) : null;
            if (DescendantFont is not null)
            {
                target = DescendantFont;
                CidSystemInfo = ReadCidSystemInfo(DescendantFont, objects);
                DefaultCidWidth = NumberOf(objects.Resolve(Get(DescendantFont, "DW")), 1000.0);
                CidWidths = ReadCidWidths(DescendantFont, objects);
            }
        }

        Descriptor = objects.ResolveAs<PdfDictionary>(Get(target, "FontDescriptor"));
        (ProgramKind, _programStream) = FindProgram(Descriptor, objects);
        IsEmbedded = ProgramKind != FontProgramKind.None;

        if (!IsEmbedded && Subtype is "Type1" or "MMType1" or "TrueType" or "Type0")
        {
            int flags = (int)NumberOf(objects.Resolve(Get(Descriptor, "Flags")), 0);
            double italicAngle = NumberOf(objects.Resolve(Get(Descriptor, "ItalicAngle")), 0);
            double weight = NumberOf(objects.Resolve(Get(Descriptor, "FontWeight")), 0);
            Substitute = RenderableFont.IsStandard14Name(StripSubset(BaseFont))
                ? StripSubset(BaseFont)
                : Standard14Substitute.Choose(BaseFont, flags, italicAngle, weight);
        }
    }

    /// <summary>The font dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The <c>/Subtype</c>: Type1, MMType1, TrueType, Type0 or Type3.</summary>
    public string Subtype { get; }

    /// <summary>The <c>/BaseFont</c> name, including any subset prefix; empty when absent.</summary>
    public string BaseFont { get; }

    /// <summary>The <c>/Encoding</c> when it is a name (for example "WinAnsiEncoding" or "Identity-H"); null otherwise.</summary>
    public string? EncodingName { get; }

    /// <summary>The font descriptor (of the descendant font for Type 0), or null.</summary>
    public PdfDictionary? Descriptor { get; }

    /// <summary>True when the PDF embeds the font program.</summary>
    public bool IsEmbedded { get; }

    /// <summary>The kind of the embedded program, or <see cref="FontProgramKind.None"/>.</summary>
    public FontProgramKind ProgramKind { get; }

    /// <summary>For a Type 0 font: the descendant CIDFont dictionary; otherwise null.</summary>
    public PdfDictionary? DescendantFont { get; }

    /// <summary>For a Type 0 font: the descendant's <c>/CIDSystemInfo</c>; otherwise null.</summary>
    public CidSystemInfo? CidSystemInfo { get; }

    /// <summary>For a Type 0 font: the descendant's <c>/W</c> widths by CID, in thousandths of an em; otherwise empty.</summary>
    public IReadOnlyDictionary<int, double> CidWidths { get; } = new Dictionary<int, double>();

    /// <summary>For a Type 0 font: the descendant's <c>/DW</c> (1000 when absent); otherwise 0.</summary>
    public double DefaultCidWidth { get; }

    /// <summary>
    /// For a font the PDF does not embed: the standard 14 font the raster
    /// renderer draws it with (chosen from the name, flags, <c>/FontWeight</c>
    /// and <c>/ItalicAngle</c>); null for an embedded font or a Type 3 font.
    /// Indic text in such a font is drawn by the raster renderer with the LiPi
    /// Sans face of its script from Chuvadi.Fonts, Bold when this name is bold.
    /// </summary>
    public string? Substitute { get; }

    /// <summary>
    /// Returns the decoded program bytes (read once and cached), or null when the
    /// font is not embedded or the stream cannot be decoded.
    /// </summary>
    /// <returns>The program, or null. The array is shared: do not change it.</returns>
    public byte[]? GetProgram()
    {
        lock (_gate)
        {
            if (!_programRead)
            {
                _programRead = true;
                if (_programStream is not null)
                {
                    try
                    {
                        _program = ContentStreamLoader.Decode(_programStream, _objects);
                    }
                    catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
                    {
                        _program = null;
                    }
                }
            }

            return _program;
        }
    }

    /// <summary>
    /// For a Type 0 font with a <c>/CIDToGIDMap</c> stream: the glyph id of each
    /// CID (index = CID). Null when the map is <c>/Identity</c>, absent, or the
    /// font is not a CIDFontType2.
    /// </summary>
    /// <returns>The map, or null. The array is shared: do not change it.</returns>
    public int[]? GetCidToGidMap()
    {
        lock (_gate)
        {
            if (!_cidToGidRead)
            {
                _cidToGidRead = true;
                PdfStream? map = DescendantFont is null ? null : _objects.ResolveAs<PdfStream>(Get(DescendantFont, "CIDToGIDMap"));
                if (map is not null)
                {
                    try
                    {
                        byte[] bytes = ContentStreamLoader.Decode(map, _objects);
                        int[] gids = new int[bytes.Length / 2];
                        for (int i = 0; i < gids.Length; i++)
                        {
                            gids[i] = (bytes[2 * i] << 8) | bytes[(2 * i) + 1];
                        }

                        _cidToGid = gids;
                    }
                    catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
                    {
                        _cidToGid = null;
                    }
                }
            }

            return _cidToGid;
        }
    }

    /// <summary>True when the Type 0 font's <c>/CIDToGIDMap</c> is the name <c>/Identity</c>.</summary>
    public bool IsCidToGidIdentity =>
        DescendantFont is not null && _objects.ResolveAs<PdfName>(Get(DescendantFont, "CIDToGIDMap"))?.Value == "Identity";

    /// <summary>True when <see cref="BaseFont"/> has a subset prefix (six capital letters and "+", §9.6.4).</summary>
    public bool IsSubset =>
        BaseFont.Length > 7 && BaseFont[6] == '+' && BaseFont[..6].All(c => c is >= 'A' and <= 'Z');

    /// <summary>
    /// The program's units per em: from the TrueType <c>head</c> table or the
    /// CFF font matrix; 1000 for Type 1, Type 3 and fonts that are not embedded.
    /// </summary>
    public int UnitsPerEm
    {
        get
        {
            EnsureRenderer();
            return _renderer?.UnitsPerEm ?? 1000;
        }
    }

    /// <summary>
    /// The embedding rights from the program's <c>OS/2</c> <c>fsType</c>
    /// (TrueType and OpenType programs); <see cref="FontEmbeddingPermission.Unknown"/>
    /// otherwise.
    /// </summary>
    public FontEmbeddingRights EmbeddingRights
    {
        get
        {
            byte[]? program = ProgramKind is FontProgramKind.TrueType or FontProgramKind.OpenType ? GetProgram() : null;
            int? fsType = program is null ? null : ReadFsType(program);
            if (fsType is not int t)
            {
                return new FontEmbeddingRights(FontEmbeddingPermission.Unknown, false, false);
            }

            FontEmbeddingPermission p = (t & 0x0F) == 0 ? FontEmbeddingPermission.Installable
                : (t & 0x08) != 0 ? FontEmbeddingPermission.Editable
                : (t & 0x04) != 0 ? FontEmbeddingPermission.PreviewAndPrint
                : FontEmbeddingPermission.Restricted;
            return new FontEmbeddingRights(p, (t & 0x0100) != 0, (t & 0x0200) != 0);
        }
    }

    /// <summary>
    /// The width of a character code in thousandths of an em: <c>/Widths</c>
    /// (with <c>/FirstChar</c>, else <c>/MissingWidth</c>) for a simple font,
    /// <c>/W</c> and <c>/DW</c> for a Type 0 font, and the standard 14 metrics
    /// for a standard 14 font without <c>/Widths</c>.
    /// </summary>
    /// <param name="code">The character code (a CID for Type 0 fonts).</param>
    /// <returns>The width.</returns>
    public double GetWidth(int code)
    {
        if (Subtype == "Type0")
        {
            return CidWidths.TryGetValue(code, out double w) ? w : DefaultCidWidth;
        }

        if (_objects.ResolveAs<PdfArray>(Get(Dictionary, "Widths")) is PdfArray widths)
        {
            int first = (int)NumberOf(_objects.Resolve(Get(Dictionary, "FirstChar")), 0);
            int index = code - first;
            if (index >= 0 && index < widths.Count)
            {
                return NumberOf(_objects.Resolve(widths[index]), 0);
            }

            return NumberOf(_objects.Resolve(Get(Descriptor, "MissingWidth")), 0);
        }

        string std = Substitute ?? StripSubset(BaseFont);
        return Standard14Widths.IsStandard14(std) ? Standard14Widths.GetWidth(std, code) : 0;
    }

    /// <summary>
    /// The glyph a character code selects in the embedded program, by the same
    /// rules the raster renderer uses: for a Type 0 font, the CID's glyph
    /// (through <c>/CIDToGIDMap</c> or the CFF charset); for a simple TrueType,
    /// OpenType or CFF font, the encoding and <c>cmap</c> rules of §9.6.6.4; for
    /// a Type 1 font, the code itself (<see cref="GetGlyphOutline"/> takes codes
    /// for Type 1). Returns -1 when the font is not embedded or the program
    /// cannot be read, and 0 (.notdef) when the code has no glyph.
    /// </summary>
    /// <param name="code">The character code (a CID for Type 0 fonts).</param>
    /// <returns>The glyph id, 0, or -1.</returns>
    public int GetGlyphId(int code)
    {
        EnsureRenderer();
        if (ProgramKind == FontProgramKind.Type1)
        {
            return _type1 is null ? -1 : code;
        }

        if (_renderer is null)
        {
            return -1;
        }

        if (Subtype == "Type0")
        {
            return EmbeddedGlyphs.ForCid(_renderer, GetCidToGidMap(), code);
        }

        int flags = (int)NumberOf(_objects.Resolve(Get(Descriptor, "Flags")), 0);
        return EmbeddedGlyphs.ForSimpleCode(_renderer, GetPdfFont(), code, (flags & 4) != 0);
    }

    /// <summary>
    /// The outline of a glyph in the embedded program, in the program's own
    /// units (<see cref="UnitsPerEm"/>), or null when the font is not embedded
    /// or the glyph cannot be read. For a Type 1 font, pass the character code.
    /// </summary>
    /// <param name="glyphId">The glyph id from <see cref="GetGlyphId"/>.</param>
    /// <returns>The outline, or null.</returns>
    public GlyphOutline? GetGlyphOutline(int glyphId)
    {
        EnsureRenderer();
        try
        {
            if (_type1 is not null)
            {
                Chuvadi.Pdf.Graphics.Path path = _type1.GetGlyphPath(glyphId, 1000, out double advance);
                return new GlyphOutline(path, new GlyphMetrics((int)Math.Round(advance), 0, 1000, default));
            }

            return _renderer?.GetGlyphOutline(glyphId);
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// For a font the PDF does not embed: the outline of a character code in
    /// <see cref="Substitute"/>, the standard 14 font the raster renderer draws
    /// it with, in a 1000-unit em. Null when there is no substitute or no
    /// glyph.
    /// </summary>
    /// <param name="code">The character code.</param>
    /// <returns>The outline, or null.</returns>
    public Chuvadi.Pdf.Graphics.Path? GetSubstituteGlyphOutline(int code)
    {
        if (Substitute is null)
        {
            return null;
        }

        string text = GetPdfFont()?.DecodeCode(code) ?? string.Empty;
        char ch = text.Length > 0 ? text[0] : (char)code;
        Chuvadi.Pdf.Graphics.Path path = Standard14Outlines.GetGlyphPath(Substitute, ch);
        return path.IsEmpty ? null : path;
    }

    private Chuvadi.Pdf.Fonts.PdfFont? GetPdfFont()
    {
        lock (_gate)
        {
            if (!_pdfFontRead)
            {
                _pdfFontRead = true;
                try
                {
                    _pdfFont = Chuvadi.Pdf.Fonts.PdfFont.FromDictionary(Dictionary, _objects);
                }
                catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
                {
                    _pdfFont = null;
                }
            }

            return _pdfFont;
        }
    }

    private void EnsureRenderer()
    {
        byte[]? program = GetProgram();
        lock (_gate)
        {
            if (_rendererRead)
            {
                return;
            }

            _rendererRead = true;
            if (program is null)
            {
                return;
            }

            try
            {
                if (ProgramKind == FontProgramKind.Type1)
                {
                    _type1 = Type1FontRenderer.Create(program, Dictionary, _objects);
                }
                else
                {
                    _renderer = new FontRenderer(program);
                }
            }
            catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
            {
                _renderer = null;
                _type1 = null;
            }
        }
    }

    // OS/2 fsType (offset 8) from an sfnt table directory, or null.
    private static int? ReadFsType(byte[] font)
    {
        if (font.Length < 12)
        {
            return null;
        }

        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (16 * i);
            if (rec + 16 > font.Length)
            {
                return null;
            }

            if (font[rec] == 'O' && font[rec + 1] == 'S' && font[rec + 2] == '/' && font[rec + 3] == '2')
            {
                int offset = (font[rec + 8] << 24) | (font[rec + 9] << 16) | (font[rec + 10] << 8) | font[rec + 11];
                return offset >= 0 && offset + 10 <= font.Length ? (font[offset + 8] << 8) | font[offset + 9] : null;
            }
        }

        return null;
    }

    internal static DisplayListFont Create(PdfDictionary dictionary, PdfObjectStore objects) => new DisplayListFont(dictionary, objects);

    private static PdfPrimitive Get(PdfDictionary? dict, string key) =>
        dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null ? value : PdfNull.Value;

    private string? NameOf(PdfDictionary dict, string key) => _objects.ResolveAs<PdfName>(Get(dict, key))?.Value;

    private static double NumberOf(PdfPrimitive value, double fallback) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => fallback,
    };

    private static string StripSubset(string name) =>
        name.Length > 7 && name[6] == '+' ? name[7..] : name;

    private static (FontProgramKind, PdfStream?) FindProgram(PdfDictionary? descriptor, PdfObjectStore objects)
    {
        if (descriptor is null)
        {
            return (FontProgramKind.None, null);
        }

        if (objects.ResolveAs<PdfStream>(Get(descriptor, "FontFile")) is PdfStream type1)
        {
            return (FontProgramKind.Type1, type1);
        }

        if (objects.ResolveAs<PdfStream>(Get(descriptor, "FontFile2")) is PdfStream trueType)
        {
            return (FontProgramKind.TrueType, trueType);
        }

        if (objects.ResolveAs<PdfStream>(Get(descriptor, "FontFile3")) is PdfStream file3)
        {
            string? sub = objects.ResolveAs<PdfName>(Get(file3.Dictionary, "Subtype"))?.Value;
            FontProgramKind kind = sub switch
            {
                "CIDFontType0C" => FontProgramKind.CidCff,
                "OpenType" => FontProgramKind.OpenType,
                _ => FontProgramKind.Cff,
            };
            return (kind, file3);
        }

        return (FontProgramKind.None, null);
    }

    private static CidSystemInfo? ReadCidSystemInfo(PdfDictionary descendant, PdfObjectStore objects)
    {
        PdfDictionary? info = objects.ResolveAs<PdfDictionary>(Get(descendant, "CIDSystemInfo"));
        if (info is null)
        {
            return null;
        }

        string registry = objects.ResolveAs<PdfString>(Get(info, "Registry"))?.ToTextString() ?? string.Empty;
        string ordering = objects.ResolveAs<PdfString>(Get(info, "Ordering"))?.ToTextString() ?? string.Empty;
        int supplement = (int)NumberOf(objects.Resolve(Get(info, "Supplement")), 0);
        return new CidSystemInfo(registry, ordering, supplement);
    }

    // /W entries: "c [w1 w2 …]" or "cFirst cLast w" (§9.7.4.3).
    private static Dictionary<int, double> ReadCidWidths(PdfDictionary descendant, PdfObjectStore objects)
    {
        Dictionary<int, double> widths = new Dictionary<int, double>();
        if (objects.ResolveAs<PdfArray>(Get(descendant, "W")) is not PdfArray w)
        {
            return widths;
        }

        int i = 0;
        while (i + 1 < w.Count)
        {
            if (objects.Resolve(w[i]) is not PdfInteger first)
            {
                break;
            }

            PdfPrimitive next = objects.Resolve(w[i + 1]);
            if (next is PdfArray list)
            {
                for (int k = 0; k < list.Count && k < 65536; k++)
                {
                    widths[first.Value + k] = NumberOf(objects.Resolve(list[k]), 0);
                }

                i += 2;
            }
            else if (next is PdfInteger last && i + 2 < w.Count)
            {
                double width = NumberOf(objects.Resolve(w[i + 2]), 0);
                for (long c = first.Value; c <= last.Value && c - first.Value < 65536; c++)
                {
                    widths[(int)c] = width;
                }

                i += 3;
            }
            else
            {
                break;
            }
        }

        return widths;
    }
}
