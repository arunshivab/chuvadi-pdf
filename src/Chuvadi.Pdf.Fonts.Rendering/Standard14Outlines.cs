// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — Standard 14 outline bundle

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GraphicsPath = Chuvadi.Pdf.Graphics.Path;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Provides glyph outlines for the PDF Standard 14 fonts from an embedded
/// resource, so they work even on hosts that lack the fonts (Blazor WASM,
/// headless servers).
/// </summary>
/// <remarks>
/// <para>
/// The bundle is generated at build time by <c>tools/build_standard14_bundle.py</c>
/// from Liberation Sans/Serif/Mono and URW StandardSymbolsPS/D050000L
/// (commercially-redistributable, Apache-2.0-compatible licenses). If the
/// developer hasn't run the build tool with the source TTFs in place, the
/// bundle ships as a header-only placeholder and outline lookups return
/// empty paths — width-only operation still works via
/// <see cref="Standard14Widths"/>.
/// </para>
/// </remarks>
public static class Standard14Outlines
{
    private static readonly Lazy<Bundle> Loaded = new(LoadBundle);

    /// <summary>Returns the outline path for <paramref name="ch"/> in the given font.</summary>
    /// <returns>An empty path when the font or character isn't in the bundle.</returns>
    public static GraphicsPath GetGlyphPath(string fontName, char ch)
    {
        ArgumentNullException.ThrowIfNull(fontName);
        Bundle b = Loaded.Value;
        if (!b.Fonts.TryGetValue(fontName, out FontEntry? entry))
        {
            return new GraphicsPath();
        }
        if (!entry.Glyphs.TryGetValue(ch, out byte[]? bytes))
        {
            return new GraphicsPath();
        }
        return DeserializeGlyph(bytes, entry.UnitsPerEm);
    }

    /// <summary>True when the bundle was built from real font data.</summary>
    public static bool BundleAvailable => Loaded.Value.HasRealOutlines;

    /// <summary>Returns the list of font names known to the bundle.</summary>
    public static IReadOnlyCollection<string> KnownFonts => Loaded.Value.Fonts.Keys;

    private static GraphicsPath DeserializeGlyph(byte[] data, int unitsPerEm)
    {
        // Normalise design-unit coordinates to a 1000-unit em so callers can
        // scale uniformly by pointSize / 1000 regardless of the substitute
        // font's native units-per-em.
        double scale = unitsPerEm > 0 ? 1000.0 / unitsPerEm : 1.0;
        GraphicsPath path = new();
        int cursor = 0;
        int cmdCount = ReadUInt16(data, ref cursor);
        for (int c = 0; c < cmdCount; c++)
        {
            byte cmd = data[cursor++];
            byte ptCount = data[cursor++];
            (double x, double y)[] pts = new (double, double)[ptCount];
            for (int p = 0; p < ptCount; p++)
            {
                short px = ReadInt16(data, ref cursor);
                short py = ReadInt16(data, ref cursor);
                pts[p] = (px * scale, py * scale);
            }
            switch (cmd)
            {
                case 0:  // MOVE
                    if (ptCount > 0) { path.MoveTo(pts[0].x, pts[0].y); }
                    break;
                case 1:  // LINE
                    if (ptCount > 0) { path.LineTo(pts[0].x, pts[0].y); }
                    break;
                case 2:  // CUBIC
                    if (ptCount >= 3)
                    {
                        path.CubicBezierTo(
                            new PointF((float)pts[0].x, (float)pts[0].y),
                            new PointF((float)pts[1].x, (float)pts[1].y),
                            new PointF((float)pts[2].x, (float)pts[2].y));
                    }
                    break;
                case 3:  // QUAD — approximate with a cubic
                    if (ptCount >= 2)
                    {
                        PointF cur = path.CurrentPoint;
                        double c1x = cur.X + 2.0 * (pts[0].x - cur.X) / 3.0;
                        double c1y = cur.Y + 2.0 * (pts[0].y - cur.Y) / 3.0;
                        double c2x = pts[1].x + 2.0 * (pts[0].x - pts[1].x) / 3.0;
                        double c2y = pts[1].y + 2.0 * (pts[0].y - pts[1].y) / 3.0;
                        path.CubicBezierTo(
                            new PointF((float)c1x, (float)c1y),
                            new PointF((float)c2x, (float)c2y),
                            new PointF((float)pts[1].x, (float)pts[1].y));
                    }
                    break;
                case 4:  // CLOSE
                    path.ClosePath();
                    break;
                default: break;
            }
        }
        return path;
    }

    private static Bundle LoadBundle()
    {
        // Load embedded resource Standard14.bin.
        Assembly asm = typeof(Standard14Outlines).Assembly;
        string[] names = asm.GetManifestResourceNames();
        string? resName = null;
        foreach (string n in names)
        {
            if (n.EndsWith("Standard14.bin", StringComparison.Ordinal))
            {
                resName = n;
                break;
            }
        }
        if (resName is null) { return Bundle.Empty(); }

        using Stream? s = asm.GetManifestResourceStream(resName);
        if (s is null) { return Bundle.Empty(); }
        using MemoryStream ms = new();
        s.CopyTo(ms);
        byte[] data = ms.ToArray();

        if (data.Length < 16) { return Bundle.Empty(); }
        // Header check
        if (data[0] != 'C' || data[1] != 'V' || data[2] != '1' || data[3] != '4')
        {
            return Bundle.Empty();
        }
        int cursor = 4;
        int version = ReadInt32(data, ref cursor);
        int numFonts = ReadInt32(data, ref cursor);
        _ = ReadInt32(data, ref cursor); // reserved
        if (version != 1 || numFonts <= 0) { return Bundle.Empty(); }

        Dictionary<string, FontEntry> fonts = new();
        bool any = false;
        for (int f = 0; f < numFonts; f++)
        {
            if (cursor + 40 > data.Length) { break; }
            string fontName = ReadAscii(data, cursor, 32);
            cursor += 32;
            int unitsPerEm = ReadInt32(data, ref cursor);
            int glyphCount = ReadInt32(data, ref cursor);
            FontEntry entry = new(unitsPerEm);
            for (int g = 0; g < glyphCount; g++)
            {
                if (cursor + 6 > data.Length) { break; }
                int charCode = ReadUInt16(data, ref cursor);
                int dataLen = ReadInt32(data, ref cursor);
                if (cursor + dataLen > data.Length) { break; }
                byte[] glyphBytes = new byte[dataLen];
                Array.Copy(data, cursor, glyphBytes, 0, dataLen);
                cursor += dataLen;
                entry.Glyphs[(char)charCode] = glyphBytes;
            }
            if (glyphCount > 0) { any = true; }
            fonts[fontName] = entry;
        }
        return new Bundle(fonts, any);
    }

    private static int ReadUInt16(byte[] data, ref int cursor)
    {
        int v = data[cursor] | (data[cursor + 1] << 8);
        cursor += 2;
        return v;
    }

    private static short ReadInt16(byte[] data, ref int cursor)
    {
        int v = data[cursor] | (data[cursor + 1] << 8);
        cursor += 2;
        return (short)v;
    }

    private static int ReadInt32(byte[] data, ref int cursor)
    {
        int v = data[cursor] | (data[cursor + 1] << 8) | (data[cursor + 2] << 16) | (data[cursor + 3] << 24);
        cursor += 4;
        return v;
    }

    private static string ReadAscii(byte[] data, int offset, int length)
    {
        int end = offset;
        while (end < offset + length && data[end] != 0) { end++; }
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private sealed class FontEntry
    {
        public int UnitsPerEm { get; }
        public Dictionary<char, byte[]> Glyphs { get; } = new();
        internal FontEntry(int unitsPerEm) { UnitsPerEm = unitsPerEm; }
    }

    private sealed class Bundle
    {
        public Dictionary<string, FontEntry> Fonts { get; }
        public bool HasRealOutlines { get; }
        internal Bundle(Dictionary<string, FontEntry> fonts, bool hasReal)
        {
            Fonts = fonts;
            HasRealOutlines = hasReal;
        }
        internal static Bundle Empty() => new(new Dictionary<string, FontEntry>(), false);
    }
}
