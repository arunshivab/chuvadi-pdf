// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9 — Text
//        PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 Fonts
// PHASE: v2.0.0 R1 D3b — combined text+glyph font API

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// A PDF font that supports both text decoding (character codes to Unicode)
/// and glyph rendering (character codes to vector outlines + metrics).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RenderableFont"/> is the public surface that the v2 reader-app
/// pipeline will use to draw text. It composes <see cref="PdfFont"/> (text
/// decoding) with a glyph outline source that varies by font kind:
/// </para>
/// <list type="bullet">
///   <item><b>Standard 14 fonts</b>: glyph outlines come from
///   <see cref="Standard14Outlines"/> (an embedded resource bundle that
///   ships independent of any host font) and widths come from
///   <see cref="Standard14Widths"/>.</item>
///   <item><b>Other fonts</b>: in v2.0.0 R1 D3b this returns empty paths and
///   approximate half-em widths. R1 D3c will add embedded-font-program
///   support so that fonts with a FontFile / FontFile2 / FontFile3 stream
///   render their real outlines.</item>
/// </list>
/// <para>
/// <see cref="RenderableFont"/> is immutable after construction. Glyph
/// outlines are produced on demand and are not cached by this type; the
/// underlying <see cref="Standard14Outlines"/> lazily loads the bundle
/// once per process.
/// </para>
/// </remarks>
public sealed class RenderableFont
{
    private static readonly HashSet<string> Std14Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "Helvetica",
        "Helvetica-Bold",
        "Helvetica-Oblique",
        "Helvetica-BoldOblique",
        "Times-Roman",
        "Times-Bold",
        "Times-Italic",
        "Times-BoldItalic",
        "Courier",
        "Courier-Bold",
        "Courier-Oblique",
        "Courier-BoldOblique",
        "Symbol",
        "ZapfDingbats",
    };

    private readonly PdfFont _pdfFont;

    private RenderableFont(string fontName, PdfFont pdfFont, int unitsPerEm, bool isStandard14)
    {
        FontName = fontName;
        _pdfFont = pdfFont;
        UnitsPerEm = unitsPerEm;
        IsStandard14 = isStandard14;
    }

    /// <summary>Gets the font's PostScript name with any subset prefix removed.</summary>
    public string FontName { get; }

    /// <summary>Gets whether this font is one of the 14 standard PDF base fonts.</summary>
    public bool IsStandard14 { get; }

    /// <summary>
    /// Gets the font's units-per-em value (1000 for Standard 14, otherwise
    /// the value reported by the embedded font program if any, else 1000).
    /// </summary>
    public int UnitsPerEm { get; }

    /// <summary>
    /// Returns true when <paramref name="fontName"/> matches one of the
    /// 14 Standard PostScript font names.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontName"/> is null.
    /// </exception>
    public static bool IsStandard14Name(string fontName)
    {
        ArgumentNullException.ThrowIfNull(fontName);
        return Std14Names.Contains(fontName);
    }

    /// <summary>
    /// Builds a <see cref="RenderableFont"/> from a font dictionary.
    /// </summary>
    /// <param name="fontDict">The font dictionary from the page Resources.</param>
    /// <param name="resolver">Used to resolve indirect object references.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontDict"/> or <paramref name="resolver"/> is null.
    /// </exception>
    public static RenderableFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(resolver);

        PdfFont pdfFont = PdfFont.FromDictionary(fontDict, resolver);
        string fontName = GetBaseFontName(fontDict);
        bool isStd14 = Std14Names.Contains(fontName);
        return new RenderableFont(fontName, pdfFont, unitsPerEm: 1000, isStandard14: isStd14);
    }

    /// <summary>
    /// Builds a <see cref="RenderableFont"/> that draws a font the PDF does
    /// not embed with a standard 14 font's outlines (since 3.30): characters
    /// are decoded through the font's own encoding and ToUnicode map, and
    /// glyph shapes come from <paramref name="standard14Name"/>
    /// (see <see cref="Standard14Substitute"/>).
    /// </summary>
    /// <param name="fontDict">The font dictionary from the page Resources.</param>
    /// <param name="resolver">Used to resolve indirect object references.</param>
    /// <param name="standard14Name">The standard 14 font whose outlines to use.</param>
    /// <returns>A font that reports <see cref="IsStandard14"/> as true.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="standard14Name"/> is not a standard 14 font name.</exception>
    public static RenderableFont AsSubstitute(PdfDictionary fontDict, IPdfObjectResolver resolver, string standard14Name)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(standard14Name);
        if (!Std14Names.Contains(standard14Name))
        {
            throw new ArgumentException($"'{standard14Name}' is not a standard 14 font name.", nameof(standard14Name));
        }

        PdfFont pdfFont = PdfFont.FromDictionary(fontDict, resolver);
        return new RenderableFont(standard14Name, pdfFont, unitsPerEm: 1000, isStandard14: true);
    }

    /// <summary>
    /// Returns a default <see cref="RenderableFont"/> equivalent to Helvetica
    /// with WinAnsi encoding. Used when no font dictionary is available.
    /// </summary>
    public static RenderableFont Default()
    {
        return new RenderableFont(
            fontName: "Helvetica",
            pdfFont: PdfFont.Default(),
            unitsPerEm: 1000,
            isStandard14: true);
    }

    // ── Glyph paths ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the glyph outline for <paramref name="charCode"/> in unscaled
    /// font design units (Y up, origin at the glyph anchor).
    /// </summary>
    /// <remarks>
    /// Returns an empty path when:
    /// <list type="bullet">
    ///   <item>The character is not present in the font.</item>
    ///   <item>The font is Standard 14 but the outline bundle has not been
    ///   built (see <see cref="Standard14Outlines.BundleAvailable"/>).</item>
    ///   <item>The font is non-Standard 14 (embedded font support arrives in D3c).</item>
    /// </list>
    /// </remarks>
    public Path GetGlyphPath(int charCode)
    {
        if (IsStandard14)
        {
            return Standard14Outlines.GetGlyphPath(FontName, (char)charCode);
        }

        return new Path();
    }

    /// <summary>
    /// Returns the glyph outline for <paramref name="charCode"/> scaled to
    /// <paramref name="pointSize"/> PDF points.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pointSize"/> is not positive.
    /// </exception>
    public Path GetGlyphPath(int charCode, double pointSize)
    {
        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        Path unscaled = GetGlyphPath(charCode);

        if (unscaled.IsEmpty)
        {
            return unscaled;
        }

        double scale = pointSize / UnitsPerEm;
        return ScalePath(unscaled, scale);
    }

    /// <summary>
    /// Returns the advance width of <paramref name="charCode"/> in PDF points
    /// at <paramref name="pointSize"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pointSize"/> is not positive.
    /// </exception>
    public double GetAdvanceWidth(int charCode, double pointSize)
    {
        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        if (IsStandard14)
        {
            int width1000 = Standard14Widths.GetWidth(FontName, charCode);
            return width1000 / 1000.0 * pointSize;
        }

        // Non-Std14 fallback until D3c adds FontFile/FontFile2/FontFile3 support.
        return pointSize * 0.5;
    }

    /// <summary>
    /// Decodes raw bytes from a text-showing operator (Tj, TJ, ', ") to a
    /// Unicode string. Delegates to the wrapped <see cref="PdfFont"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="bytes"/> is null.
    /// </exception>
    public string DecodeText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return _pdfFont.Decode(bytes);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string GetBaseFontName(PdfDictionary fontDict)
    {
        if (!fontDict.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? baseFont))
        {
            return "Unknown";
        }

        if (baseFont is not PdfName name)
        {
            return "Unknown";
        }

        // PDF font subset prefixes look like "ABCDEF+FontName"; strip prefix.
        string raw = name.Value;
        int plus = raw.IndexOf('+');
        return plus >= 0 && plus < raw.Length - 1 ? raw[(plus + 1)..] : raw;
    }

    private static Path ScalePath(Path source, double scale)
    {
        Path result = new Path();

        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    result.MoveTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;

                case PathSegmentKind.LineTo:
                    result.LineTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;

                case PathSegmentKind.CubicBezierTo:
                    result.CubicBezierTo(
                        new PointF(seg.P0.X * scale, seg.P0.Y * scale),
                        new PointF(seg.P1.X * scale, seg.P1.Y * scale),
                        new PointF(seg.P2.X * scale, seg.P2.Y * scale));
                    break;

                case PathSegmentKind.ClosePath:
                    result.ClosePath();
                    break;
            }
        }

        return result;
    }
}
