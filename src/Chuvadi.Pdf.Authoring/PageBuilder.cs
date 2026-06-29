// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Per-page drawing API. All coordinates use top-left origin (Y increases
/// downward), units are PDF points (1 pt = 1/72 inch).
/// </summary>
public sealed class PageBuilder
{
    private readonly ContentStreamWriter _w;
    private readonly CustomFontRegistry? _customFonts;
    private readonly LipiFontSet? _lipi;
    internal List<HyperlinkRect> Hyperlinks { get; } = new();
    internal List<ImageRef> Images { get; } = new();
    internal HashSet<string> Fonts { get; } = new();

    // gsKey -> constant alpha (0..1) for image-overlay opacity; assembled into
    // the page's /ExtGState resource by PdfDocumentBuilder.
    internal Dictionary<string, double> ExtGStateAlphas { get; } = new();

    /// <summary>Page width in points.</summary>
    public double Width { get; }

    /// <summary>Page height in points.</summary>
    public double Height { get; }

    internal PageBuilder(PageSize size)
        : this(size, null, null)
    {
    }

    internal PageBuilder(PageSize size, CustomFontRegistry? customFonts)
        : this(size, customFonts, null)
    {
    }

    internal PageBuilder(PageSize size, CustomFontRegistry? customFonts, LipiFontSet? lipi)
    {
        Width = size.Width;
        Height = size.Height;
        _customFonts = customFonts;
        _lipi = lipi;
        _w = new ContentStreamWriter(Height);
    }

    internal byte[] ContentStream() => _w.ToBytes();

    // ── Text ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a single line of text. The top of the text aligns to <paramref name="y"/>.
    /// </summary>
    public PageBuilder DrawText(
        string text, double x, double y, string font, double size, Color color)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        if (_lipi is not null && _customFonts is not null
            && string.Equals(font, LipiFontSet.LogicalName, StringComparison.Ordinal))
        {
            DrawLipiText(text, x, y, size, color);
            return this;
        }

        Fonts.Add(font);
        _w.PushState();
        _w.SetFillRgb(color);
        if (_customFonts is not null && _customFonts.TryGet(font, out CustomFont custom))
        {
            _w.ShowGlyphsAt(FontKey(font), size, x, y, EncodeGlyphs(text, custom));
        }
        else
        {
            _w.ShowTextAt(FontKey(font), size, x, y, text);
        }

        _w.PopState();
        return this;
    }

    // Draws text with the automatic LiPi font, splitting it into per-script runs
    // and drawing each with the matching embedded LiPi face. Selection only; no
    // complex shaping is applied (glyphs are emitted in logical order).
    private void DrawLipiText(string text, double x, double y, double size, Color color)
    {
        double penX = x;
        foreach (ScriptRun run in ScriptClassifier.Split(text))
        {
            CustomFont face = EnsureLipiFace(run.Script);
            string faceName = LipiFontSet.FaceName(run.Script);
            Fonts.Add(faceName);

            _w.PushState();
            _w.SetFillRgb(color);
            _w.ShowGlyphsAt(FontKey(faceName), size, penX, y, EncodeGlyphs(run.Text, face));
            _w.PopState();

            penX += MeasureLipiRun(run.Text, face, size);
        }
    }

    private CustomFont EnsureLipiFace(LipiScript script)
    {
        string faceName = LipiFontSet.FaceName(script);
        if (_customFonts!.TryGet(faceName, out CustomFont existing))
        {
            return existing;
        }

        _customFonts.Register(faceName, _lipi!.GetFontProgram(script));
        _customFonts.TryGet(faceName, out CustomFont registered);
        return registered;
    }

    private static double MeasureLipiRun(string text, CustomFont face, double size)
    {
        double scale = size / face.Loader.UnitsPerEm;
        double total = 0.0;
        int i = 0;
        while (i < text.Length)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
            int gid = face.Loader.GetGlyphIndex(codepoint);
            if (gid > 0)
            {
                total += face.Loader.GetGlyphMetrics(gid).AdvanceWidth * scale;
            }
        }

        return total;
    }

    /// <summary>
    /// Draws a pre-shaped glyph run with a registered custom (TrueType) font.
    /// Each glyph is placed at an absolute position derived from its advance and
    /// offsets, preserving the output of an external shaper. Advances and offsets
    /// are in 1000-units-per-em text space.
    /// </summary>
    /// <param name="glyphs">The shaped glyphs, in visual order.</param>
    /// <param name="x">The pen x position (top-left origin).</param>
    /// <param name="y">The baseline-top y position.</param>
    /// <param name="font">The name of a registered custom TrueType font.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="color">The fill color.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyphs"/> or <paramref name="font"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="font"/> is not a registered custom font.</exception>
    public PageBuilder DrawShapedRun(
        IReadOnlyList<ShapedGlyph> glyphs, double x, double y, string font, double size, Color color)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(font);
        if (_customFonts is null || !_customFonts.TryGet(font, out CustomFont custom))
        {
            throw new InvalidOperationException(
                $"DrawShapedRun requires a registered custom TrueType font; '{font}' is not registered.");
        }

        Fonts.Add(font);
        double scale = size / 1000.0;
        double penX = x;
        foreach (ShapedGlyph glyph in glyphs)
        {
            custom.UsedGlyphs.Add(glyph.GlyphId);
            _w.PushState();
            _w.SetFillRgb(color);
            _w.ShowGlyphsAt(FontKey(font), size, penX + (glyph.XOffset * scale), y - (glyph.YOffset * scale), GlyphHex(glyph.GlyphId));
            _w.PopState();
            penX += glyph.XAdvance * scale;
        }

        return this;
    }

    private static string GlyphHex(int gid)
        => ((gid >> 8) & 0xFF).ToString("X2", CultureInfo.InvariantCulture)
            + (gid & 0xFF).ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>
    /// Draws word-wrapped text inside a rectangle. Returns a result indicating
    /// whether all text fit and what (if any) remains.
    /// </summary>
    public TextBlockResult DrawTextBlock(
        string text,
        double x, double y, double width, double height,
        string font, double size, Color color,
        TextAlignment align = TextAlignment.Left,
        double lineHeight = 1.2)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        Fonts.Add(font);

        List<string> lines = WordWrap(text, font, size, width);
        double lineGap = size * lineHeight;
        double yCursor = y;
        double bottom = y + height;
        int linesDrawn = 0;

        foreach (string line in lines)
        {
            if (yCursor + size > bottom) { break; }

            double drawX = x;
            if (align == TextAlignment.Center)
            {
                drawX = x + (width - FontMetrics.MeasureText(line, font, size)) / 2.0;
            }
            else if (align == TextAlignment.Right)
            {
                drawX = x + width - FontMetrics.MeasureText(line, font, size);
            }

            _w.PushState();
            _w.SetFillRgb(color);
            _w.ShowTextAt(FontKey(font), size, drawX, yCursor, line);
            _w.PopState();

            yCursor += lineGap;
            linesDrawn++;
        }

        string remaining = string.Empty;
        if (linesDrawn < lines.Count)
        {
            remaining = string.Join(' ', lines.GetRange(linesDrawn, lines.Count - linesDrawn));
        }

        return new TextBlockResult
        {
            HasOverflow = linesDrawn < lines.Count,
            RemainingText = remaining,
            NextYFromTop = yCursor,
        };
    }

    /// <summary>
    /// Draws text and registers a clickable hyperlink covering the text bounds.
    /// </summary>
    public PageBuilder DrawHyperlink(
        string text, double x, double y, string font, double size, Color color, string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        DrawText(text, x, y, font, size, color);
        double w = FontMetrics.MeasureText(text, font, size);
        // Hyperlink rect uses bottom-left coords (PDF native for annotations).
        double yBottom = _w.FlipY(y + size);
        Hyperlinks.Add(new HyperlinkRect(x, yBottom, w, size, uri));
        return this;
    }

    // ── Primitives ────────────────────────────────────────────────────────

    /// <summary>Draws a line from (x1, y1) to (x2, y2).</summary>
    public PageBuilder DrawLine(
        double x1, double y1, double x2, double y2, Color color, double width = 1.0)
    {
        _w.PushState();
        _w.SetStrokeRgb(color);
        _w.SetLineWidth(width);
        _w.MoveToTopLeft(x1, y1);
        _w.LineToTopLeft(x2, y2);
        _w.Stroke();
        _w.PopState();
        return this;
    }

    /// <summary>
    /// Draws a rectangle. Supply at least one of <paramref name="fill"/>
    /// or <paramref name="stroke"/>.
    /// </summary>
    public PageBuilder DrawRectangle(
        double x, double y, double width, double height,
        Color? fill = null, Color? stroke = null, double strokeWidth = 1.0)
    {
        _w.PushState();
        if (fill is Color f) { _w.SetFillRgb(f); }
        if (stroke is Color s)
        {
            _w.SetStrokeRgb(s);
            _w.SetLineWidth(strokeWidth);
        }
        _w.RectTopLeft(x, y, width, height);
        if (fill is not null && stroke is not null) { _w.FillAndStroke(); }
        else if (fill is not null) { _w.Fill(); }
        else if (stroke is not null) { _w.Stroke(); }
        _w.PopState();
        return this;
    }

    /// <summary>
    /// Draws an arbitrary <see cref="Path"/> (lines and cubic Béziers). Supply at
    /// least one of <paramref name="fill"/> or <paramref name="stroke"/>; a path
    /// with neither, or an empty path, paints nothing. <paramref name="fillRule"/>
    /// selects non-zero winding (default) or even-odd filling.
    /// </summary>
    public PageBuilder DrawPath(Path path, Color? fill = null, Color? stroke = null, double strokeWidth = 1.0, FillRule fillRule = FillRule.NonZeroWinding)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty || (fill is null && stroke is null)) { return this; }

        _w.PushState();
        if (fill is Color f) { _w.SetFillRgb(f); }
        if (stroke is Color s)
        {
            _w.SetStrokeRgb(s);
            _w.SetLineWidth(strokeWidth);
        }

        foreach (PathSegment segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    _w.MoveToTopLeft(segment.P0.X, segment.P0.Y);
                    break;
                case PathSegmentKind.LineTo:
                    _w.LineToTopLeft(segment.P0.X, segment.P0.Y);
                    break;
                case PathSegmentKind.CubicBezierTo:
                    _w.CurveToTopLeft(
                        segment.P0.X, segment.P0.Y,
                        segment.P1.X, segment.P1.Y,
                        segment.P2.X, segment.P2.Y);
                    break;
                case PathSegmentKind.ClosePath:
                    _w.ClosePath();
                    break;
            }
        }

        bool evenOdd = fillRule == FillRule.EvenOdd;
        if (fill is not null && stroke is not null)
        {
            if (evenOdd) { _w.FillAndStrokeEvenOdd(); } else { _w.FillAndStroke(); }
        }
        else if (fill is not null)
        {
            if (evenOdd) { _w.FillEvenOdd(); } else { _w.Fill(); }
        }
        else
        {
            _w.Stroke();
        }

        _w.PopState();
        return this;
    }

    // ── Images ────────────────────────────────────────────────────────────

    /// <summary>
    /// Embeds an image and draws it at the given top-left rectangle.
    /// Supports JPEG, PNG, TIFF, and BMP; images with an alpha channel are
    /// embedded with a soft mask so transparency is preserved.
    /// </summary>
    public PageBuilder DrawImage(
        byte[] imageBytes, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, imageBytes, null));
        _w.DrawImage(key, x, y, width, height);
        return this;
    }

    /// <summary>
    /// Embeds an already-decoded image frame and draws it at the given
    /// top-left rectangle. Useful for frames produced by the
    /// <c>Chuvadi.Pdf.Images</c> decoders — for example one page of a
    /// multi-frame TIFF.
    /// </summary>
    public PageBuilder DrawImage(
        ImageFrame image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, null, image));
        _w.DrawImage(key, x, y, width, height);
        return this;
    }

    /// <summary>
    /// Embeds an image and draws it at the given top-left rectangle with a
    /// constant <paramref name="opacity"/> (0 fully transparent, 1 fully opaque).
    /// Any alpha channel in the image is still honoured via its soft mask; this
    /// opacity multiplies on top of it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="opacity"/> is outside [0, 1].
    /// </exception>
    public PageBuilder DrawImage(byte[] imageBytes, double x, double y, double width, double height, double opacity)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ValidateOpacity(opacity);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, imageBytes, null));
        _w.DrawImage(key, x, y, width, height, RegisterAlpha(opacity));
        return this;
    }

    /// <summary>
    /// Embeds an already-decoded image frame and draws it at the given top-left
    /// rectangle with a constant <paramref name="opacity"/> (0 fully transparent,
    /// 1 fully opaque). Any alpha channel in the frame is still honoured via its
    /// soft mask; this opacity multiplies on top of it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="opacity"/> is outside [0, 1].
    /// </exception>
    public PageBuilder DrawImage(ImageFrame image, double x, double y, double width, double height, double opacity)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateOpacity(opacity);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, null, image));
        _w.DrawImage(key, x, y, width, height, RegisterAlpha(opacity));
        return this;
    }

    // ── Tables (handled by TableBuilder; this exposes the entry) ──────────

    /// <summary>
    /// Begins a fluent table at (x, y) with the given total width.
    /// Call <see cref="TableBuilder.Render"/> when done configuring.
    /// </summary>
    public TableBuilder DrawTable(double x, double y, double width)
        => new(this, x, y, width);

    // ── Internals ─────────────────────────────────────────────────────────

    internal ContentStreamWriter Writer => _w;

    internal static string FontKey(string fontName) => fontName.Replace("-", string.Empty);

    private static void ValidateOpacity(double opacity)
    {
        if (opacity < 0.0 || opacity > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity), opacity, "Opacity must be in [0, 1].");
        }
    }

    // Registers (or reuses) an ExtGState constant-alpha entry and returns its
    // resource key. Values are de-duplicated so repeated opacities share one
    // graphics-state object.
    private string RegisterAlpha(double opacity)
    {
        foreach (KeyValuePair<string, double> existing in ExtGStateAlphas)
        {
            if (existing.Value == opacity)
            {
                return existing.Key;
            }
        }

        string key = $"GsA{ExtGStateAlphas.Count}";
        ExtGStateAlphas[key] = opacity;
        return key;
    }

    /// <summary>
    /// Maps the text's Unicode code points to glyph ids via the font's cmap,
    /// records them as used, and returns the concatenated two-byte hex GID
    /// string for an Identity-H Tj operand. Code points with no glyph map to
    /// glyph 0 (.notdef). This emits glyphs in logical order without complex
    /// shaping (no GSUB/GPOS or reordering).
    /// </summary>
    private static string EncodeGlyphs(string text, CustomFont custom)
    {
        StringBuilder sb = new StringBuilder(text.Length * 4);
        int i = 0;
        while (i < text.Length)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            i += char.IsSurrogatePair(text, i) ? 2 : 1;
            custom.UsedCodepoints.Add(codepoint);
            int gid = custom.Loader.GetGlyphIndex(codepoint);
            if (gid < 0)
            {
                gid = 0;
            }

            sb.Append(((gid >> 8) & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
            sb.Append((gid & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static List<string> WordWrap(string text, string font, double size, double maxWidth)
    {
        List<string> lines = new();
        string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
        foreach (string para in paragraphs)
        {
            if (para.Length == 0) { lines.Add(string.Empty); continue; }
            string[] words = para.Split(' ');
            System.Text.StringBuilder current = new();
            foreach (string word in words)
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (FontMetrics.MeasureText(candidate, font, size) <= maxWidth)
                {
                    if (current.Length > 0) { current.Append(' '); }
                    current.Append(word);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    current.Append(word);
                }
            }
            if (current.Length > 0) { lines.Add(current.ToString()); }
        }
        return lines;
    }
}

/// <summary>
/// Internal: an image referenced from a page's content stream — either raw
/// encoded bytes or an already-decoded frame (exactly one is non-null).
/// </summary>
internal sealed record ImageRef(string Key, byte[]? Bytes, ImageFrame? Frame);
