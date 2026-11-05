// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — text in generated appearances (D-146, D-152): the /DA font
//        for the characters it can show, and the LiPi Sans face of each
//        character's script (Chuvadi.Fonts, matching weight and style) for the
//        rest, with a FontSubstituted warning.
// SPEC:  PDF 32000-1:2008 §12.7.3.3 (variable text, /DA), §9.7 (Type 0 fonts),
//        §9.10.3 (ToUnicode CMaps).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Pdf.Forms.Appearance;

/// <summary>
/// Lays out and writes text for one generated appearance. The primary font is
/// the one <c>/DA</c> names (from <c>/DR</c>, or a standard 14 font for the
/// usual short names); characters it cannot encode are drawn with LiPi Sans.
/// </summary>
internal sealed class AppearanceText
{
    // The usual /DA short names of the standard 14 fonts (Acrobat's /DR names).
    private static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Helv"] = "Helvetica",
        ["HeBo"] = "Helvetica-Bold",
        ["HeOb"] = "Helvetica-Oblique",
        ["HeBO"] = "Helvetica-BoldOblique",
        ["TiRo"] = "Times-Roman",
        ["TiBo"] = "Times-Bold",
        ["TiIt"] = "Times-Italic",
        ["TiBI"] = "Times-BoldItalic",
        ["Cour"] = "Courier",
        ["CoBo"] = "Courier-Bold",
        ["CoOb"] = "Courier-Oblique",
        ["CoBO"] = "Courier-BoldOblique",
        ["Symb"] = "Symbol",
        ["ZaDb"] = "ZapfDingbats",
    };

    private static readonly ConcurrentDictionary<string, FontFace?> Faces = new ConcurrentDictionary<string, FontFace?>(StringComparer.Ordinal);

    private readonly PdfObjectStore _objects;
    private readonly PdfDictionary _fontResources;
    private readonly string _primaryName;
    private readonly PdfFont? _primaryFont;
    private readonly string _std14Name;
    private readonly bool _primaryHasWidths;
    private readonly Dictionary<string, int> _primaryCodes = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly bool _bold;
    private readonly bool _italic;
    private readonly Dictionary<string, Fallback> _fallbacks = new Dictionary<string, Fallback>(StringComparer.Ordinal);
    private readonly List<string> _substitutions = new List<string>();

    internal AppearanceText(PdfObjectStore objects, PdfDictionary resources, PdfDictionary? drFonts, string fontName, double size)
    {
        _objects = objects;
        Size = size;
        _fontResources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), _fontResources);

        PdfDictionary? fontDict = drFonts is not null && drFonts.TryGetValue(PdfName.Intern(fontName), out PdfPrimitive? f)
            ? objects.ResolveAs<PdfDictionary>(f ?? PdfNull.Value)
            : null;
        if (fontDict is null)
        {
            string std = ShortNames.TryGetValue(fontName, out string? n) ? n : "Helvetica";
            fontDict = Standard14Dictionary(std);
        }

        // Resource names of generated appearances start with "Cv" so they never
        // meet a page's own font names in the builders' per-name caches.
        _primaryName = "Cv" + (fontName.Length > 0 ? fontName : "Helv");
        _fontResources.Set(PdfName.Intern(_primaryName), fontDict);
        string baseFont = objects.ResolveAs<PdfName>(Get(fontDict, "BaseFont"))?.Value ?? "Helvetica";
        PdfDictionary? descriptor = objects.ResolveAs<PdfDictionary>(Get(fontDict, "FontDescriptor"));
        string subtype = objects.ResolveAs<PdfName>(Get(fontDict, "Subtype"))?.Value ?? string.Empty;
        string stripped = baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;
        _std14Name = RenderableFont.IsStandard14Name(stripped)
            ? stripped
            : Standard14Substitute.Choose(
                baseFont,
                (int)Number(Get(descriptor, "Flags")),
                Number(Get(descriptor, "ItalicAngle")),
                Number(Get(descriptor, "FontWeight"))) ?? "Helvetica";
        _bold = _std14Name.Contains("Bold", StringComparison.Ordinal);
        _italic = _std14Name.Contains("Italic", StringComparison.Ordinal) || _std14Name.Contains("Oblique", StringComparison.Ordinal);

        // A simple font: every code 0-255 its encoding maps to a character.
        if (subtype is "Type1" or "TrueType" or "MMType1")
        {
            try
            {
                _primaryFont = PdfFont.FromDictionary(fontDict, objects);
                for (int code = 255; code >= 1; code--)
                {
                    string u = _primaryFont.DecodeCode(code);
                    if (u.Length > 0)
                    {
                        _primaryCodes[u] = code;
                    }
                }

                _primaryHasWidths = _primaryFont.TryMeasure(new byte[] { 0x20 }, out _, out _, out _);
            }
            catch (FontException)
            {
                _primaryFont = null;
            }
        }
    }

    /// <summary>The font size.</summary>
    internal double Size { get; }

    /// <summary>The font's ascent as a fraction of the size (0.72 to 0.95 for most fonts).</summary>
    internal double Ascent => _std14Name.StartsWith("Courier", StringComparison.Ordinal) ? 0.629 : _std14Name.StartsWith("Times", StringComparison.Ordinal) ? 0.683 : 0.718;

    /// <summary>The font's descent as a negative fraction of the size.</summary>
    internal double Descent => _std14Name.StartsWith("Courier", StringComparison.Ordinal) ? -0.157 : _std14Name.StartsWith("Times", StringComparison.Ordinal) ? -0.217 : -0.207;

    /// <summary>The fallback faces used, for the FontSubstituted warning (empty when none).</summary>
    internal IReadOnlyList<string> Substitutions => _substitutions;

    /// <summary>Text the fallback faces could not show either (drawn as .notdef).</summary>
    internal bool HadMissing { get; private set; }

    // The style the /DA font implies, for choosing the LiPi face.
    internal bool Bold => _bold;

    internal bool Italic => _italic;

    /// <summary>Returns the width of <paramref name="text"/> at <see cref="Size"/>.</summary>
    internal double Measure(string text)
    {
        double width = 0;
        foreach (Segment seg in Segments(text))
        {
            width += seg.Width;
        }

        return width;
    }

    /// <summary>Writes <paramref name="text"/> starting at (x, baseline) inside a BT/ET block.</summary>
    internal void Show(AppearanceContent content, PdfColorSpec color, string text, double x, double baseline)
    {
        if (text.Length == 0)
        {
            return;
        }

        content.Op("BT");
        if (!color.IsEmpty)
        {
            content.Fill(color);
        }

        double pen = x;
        foreach (Segment seg in Segments(text))
        {
            if (seg.Fallback is null)
            {
                content.Raw(AppearanceContent.Name(_primaryName) + " " + AppearanceContent.Num(Size) + " Tf\n");
                content.Op("Tm", 1, 0, 0, 1, pen, baseline);
                content.ShowHex(seg.Codes);
                pen += seg.Width;
                continue;
            }

            content.Raw(AppearanceContent.Name(seg.Fallback.ResourceName) + " " + AppearanceContent.Num(Size) + " Tf\n");
            foreach (ShapedGlyph g in seg.Glyphs!)
            {
                content.Op("Tm", 1, 0, 0, 1, pen + (g.XOffset * Size / 1000.0), baseline + (g.YOffset * Size / 1000.0));
                content.ShowHex(new[] { (byte)(g.GlyphId >> 8), (byte)g.GlyphId });
                pen += g.XAdvance * Size / 1000.0;
            }
        }

        content.Op("ET");
    }

    /// <summary>Completes the fallback font dictionaries (widths and ToUnicode maps).</summary>
    internal void Finish()
    {
        foreach (Fallback fb in _fallbacks.Values)
        {
            fb.Complete();
        }
    }

    // One run of text in one font.
    private sealed record Segment(Fallback? Fallback, byte[] Codes, IReadOnlyList<ShapedGlyph>? Glyphs, double Width);

    private IEnumerable<Segment> Segments(string text)
    {
        List<Segment> result = new List<Segment>();
        StringBuilder primary = new StringBuilder();
        StringBuilder other = new StringBuilder();
        string? otherKey = null;
        LipiScript otherScript = LipiScript.Latin;

        void FlushPrimary()
        {
            if (primary.Length == 0)
            {
                return;
            }

            string s = primary.ToString();
            List<byte> codes = new List<byte>();
            double w = 0;
            foreach (string element in Elements(s))
            {
                int code = _primaryCodes[element];
                codes.Add((byte)code);
                w += PrimaryWidth(code);
            }

            result.Add(new Segment(null, codes.ToArray(), null, w));
            primary.Clear();
        }

        void FlushOther()
        {
            if (other.Length == 0 || otherKey is null)
            {
                return;
            }

            Fallback fb = GetFallback(otherKey, otherScript);
            string s = other.ToString();
            IReadOnlyList<ShapedGlyph> glyphs = fb.Shape(s, otherScript);
            double w = 0;
            foreach (ShapedGlyph g in glyphs)
            {
                w += g.XAdvance * Size / 1000.0;
            }

            result.Add(new Segment(fb, Array.Empty<byte>(), glyphs, w));
            other.Clear();
        }

        foreach (string element in Elements(text))
        {
            bool joiner = element is "‌" or "‍";
            if (!joiner && _primaryCodes.ContainsKey(element) && _primaryFont is not null)
            {
                FlushOther();
                primary.Append(element);
                continue;
            }

            LipiScript script = joiner ? otherScript : ScriptClassifier.Classify(char.ConvertToUtf32(element, 0));
            string key = FallbackKey(script);
            if (key != otherKey)
            {
                FlushOther();
            }

            FlushPrimary();
            otherKey = key;
            otherScript = script;
            other.Append(element);
        }

        FlushPrimary();
        FlushOther();
        return result;
    }

    private double PrimaryWidth(int code)
    {
        if (_primaryHasWidths && _primaryFont is not null
            && _primaryFont.TryMeasure(new[] { (byte)code }, out double w, out _, out _))
        {
            return w / 1000.0 * Size;
        }

        return Standard14Widths.GetWidth(_std14Name, code) / 1000.0 * Size;
    }

    // Text elements: a character, or a surrogate pair.
    private static IEnumerable<string> Elements(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return text.Substring(i, 2);
                i++;
            }
            else
            {
                yield return text[i].ToString();
            }
        }
    }

    // The Chuvadi.Fonts file for a script and the /DA font's style (D-152):
    // Latin has four styles; the Indic scripts have Regular and Bold, upright.
    private string FallbackKey(LipiScript script)
    {
        if (script == LipiScript.Latin)
        {
            string style = _bold && _italic ? "BoldItalic" : _bold ? "Bold" : _italic ? "Italic" : "Regular";
            return "LiPi-Sans-Latin-" + style + ".ttf";
        }

        return "LiPi-Sans-" + script.ToString() + (_bold ? "-Bold.ttf" : "-Regular.ttf");
    }

    private Fallback GetFallback(string fileName, LipiScript script)
    {
        if (_fallbacks.TryGetValue(fileName, out Fallback? existing))
        {
            return existing;
        }

        FontFace? face = Faces.GetOrAdd(fileName, LoadFace);
        string resource = "CvLiPi" + (_fallbacks.Count + 1).ToString(CultureInfo.InvariantCulture);
        Fallback fb = new Fallback(face, resource, fileName, this);
        _fallbacks[fileName] = fb;
        if (face is not null)
        {
            _substitutions.Add(fileName);
        }

        _fontResources.Set(PdfName.Intern(resource), fb.Dictionary);
        _ = script;
        return fb;
    }

    private static FontFace? LoadFace(string fileName)
    {
        try
        {
            return FontPack.Find(fileName) is null ? null : FontFace.Load(FontPack.ReadAllBytes(fileName));
        }
        catch (FontRenderingException)
        {
            return null;
        }
    }

    private static PdfDictionary Standard14Dictionary(string name)
    {
        PdfDictionary d = new PdfDictionary();
        d.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        d.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        d.Set(PdfName.Intern("BaseFont"), PdfName.Intern(name));
        if (name is not ("Symbol" or "ZapfDingbats"))
        {
            d.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
        }

        return d;
    }

    private static PdfPrimitive Get(PdfDictionary? dict, string key) =>
        dict is not null && dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is not null ? v : PdfNull.Value;

    private double Number(PdfPrimitive value) => _objects.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // A LiPi Sans face as an in-memory Type 0 font (Identity-H, glyph ids as CIDs).
    private sealed class Fallback
    {
        private readonly FontFace? _face;
        private readonly AppearanceText _owner;
        private readonly SortedDictionary<int, string> _toUnicode = new SortedDictionary<int, string>();
        private readonly SortedSet<int> _used = new SortedSet<int>();
        private readonly PdfDictionary _cidFont = new PdfDictionary();

        internal Fallback(FontFace? face, string resourceName, string fileName, AppearanceText owner)
        {
            _face = face;
            _owner = owner;
            ResourceName = resourceName;
            Dictionary = new PdfDictionary();
            string psName = face?.PostScriptName ?? fileName.Replace(".ttf", string.Empty, StringComparison.Ordinal);
            Dictionary.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
            Dictionary.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type0"));
            Dictionary.Set(PdfName.Intern("BaseFont"), PdfName.Intern(psName));
            Dictionary.Set(PdfName.Intern("Encoding"), PdfName.Intern("Identity-H"));
            _cidFont.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
            _cidFont.Set(PdfName.Intern("Subtype"), PdfName.Intern("CIDFontType2"));
            _cidFont.Set(PdfName.Intern("BaseFont"), PdfName.Intern(psName));
            PdfDictionary info = new PdfDictionary();
            info.Set(PdfName.Intern("Registry"), new PdfString("Adobe"));
            info.Set(PdfName.Intern("Ordering"), new PdfString("Identity"));
            info.Set(PdfName.Intern("Supplement"), 0);
            _cidFont.Set(PdfName.Intern("CIDSystemInfo"), info);
            _cidFont.Set(PdfName.Intern("CIDToGIDMap"), PdfName.Intern("Identity"));
            if (face is not null)
            {
                double k = 1000.0 / face.UnitsPerEm;
                PdfDictionary fd = new PdfDictionary();
                fd.Set(PdfName.Intern("Type"), PdfName.Intern("FontDescriptor"));
                fd.Set(PdfName.Intern("FontName"), PdfName.Intern(psName));
                fd.Set(PdfName.Intern("Flags"), 32);
                fd.Set(PdfName.Intern("FontBBox"), new PdfArray(new PdfPrimitive[]
                {
                    new PdfReal(face.FontBounds.XMin * k), new PdfReal(face.FontBounds.YMin * k),
                    new PdfReal(face.FontBounds.XMax * k), new PdfReal(face.FontBounds.YMax * k),
                }));
                fd.Set(PdfName.Intern("ItalicAngle"), 0);
                fd.Set(PdfName.Intern("Ascent"), new PdfReal(face.HheaAscender * k));
                fd.Set(PdfName.Intern("Descent"), new PdfReal(face.HheaDescender * k));
                fd.Set(PdfName.Intern("CapHeight"), new PdfReal(face.HheaAscender * k));
                fd.Set(PdfName.Intern("StemV"), 80);
                PdfDictionary ff = new PdfDictionary();
                ff.Set(PdfName.Intern("Length"), face.FontProgram.Length);
                ff.Set(PdfName.Intern("Length1"), face.FontProgram.Length);
                fd.Set(PdfName.Intern("FontFile2"), new PdfStream(ff, face.FontProgram));
                _cidFont.Set(PdfName.Intern("FontDescriptor"), fd);
            }

            Dictionary.Set(PdfName.Intern("DescendantFonts"), new PdfArray(new PdfPrimitive[] { _cidFont }));
        }

        internal string ResourceName { get; }

        internal PdfDictionary Dictionary { get; }

        internal IReadOnlyList<ShapedGlyph> Shape(string text, LipiScript script)
        {
            IReadOnlyList<ShapedGlyph> glyphs;
            if (_face is null)
            {
                glyphs = NotDef(text);
            }
            else
            {
                try
                {
                    glyphs = TextShaper.Shape(_face, text, script);
                }
                catch (Exception ex) when (ex is ArgumentException or FontRenderingException or InvalidOperationException)
                {
                    glyphs = NotDef(text);
                }
            }

            bool missing = false;
            for (int i = 0; i < glyphs.Count; i++)
            {
                ShapedGlyph g = glyphs[i];
                _used.Add(g.GlyphId);
                if (g.GlyphId == 0)
                {
                    missing = true;
                }

                if (!_toUnicode.ContainsKey(g.GlyphId) && g.GlyphId != 0 && (i == 0 || glyphs[i - 1].Cluster != g.Cluster))
                {
                    int end = text.Length;
                    for (int j = i + 1; j < glyphs.Count; j++)
                    {
                        if (glyphs[j].Cluster > g.Cluster)
                        {
                            end = glyphs[j].Cluster;
                            break;
                        }
                    }

                    if (g.Cluster < end && g.Cluster < text.Length)
                    {
                        _toUnicode[g.GlyphId] = text[g.Cluster..end];
                    }
                }
            }

            if (missing || _face is null)
            {
                _owner.HadMissing = true;
            }

            return glyphs;
        }

        internal void Complete()
        {
            PdfArray w = new PdfArray();
            foreach (int gid in _used)
            {
                double advance = _face is null ? 500 : _face.GetAdvanceWidth(gid) * 1000.0 / _face.UnitsPerEm;
                w.Add(new PdfInteger(gid));
                w.Add(new PdfArray(new PdfPrimitive[] { new PdfReal(Math.Round(advance, 2)) }));
            }

            _cidFont.Set(PdfName.Intern("W"), w);
            StringBuilder cmap = new StringBuilder();
            cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
            cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
            cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
            cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            List<KeyValuePair<int, string>> entries = new List<KeyValuePair<int, string>>(_toUnicode);
            for (int start = 0; start < entries.Count; start += 100)
            {
                int n = Math.Min(100, entries.Count - start);
                cmap.Append(n.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
                for (int i = start; i < start + n; i++)
                {
                    cmap.Append('<').Append(entries[i].Key.ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                    foreach (char c in entries[i].Value)
                    {
                        cmap.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }

                    cmap.Append(">\n");
                }

                cmap.Append("endbfchar\n");
            }

            cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            byte[] bytes = Encoding.ASCII.GetBytes(cmap.ToString());
            PdfDictionary sd = new PdfDictionary();
            sd.Set(PdfName.Intern("Length"), bytes.Length);
            Dictionary.Set(PdfName.Intern("ToUnicode"), new PdfStream(sd, bytes));
        }

        private static IReadOnlyList<ShapedGlyph> NotDef(string text)
        {
            List<ShapedGlyph> list = new List<ShapedGlyph>();
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsLowSurrogate(text[i]))
                {
                    list.Add(new ShapedGlyph(0, 500, 0, 0, i));
                }
            }

            return list;
        }
    }
}
