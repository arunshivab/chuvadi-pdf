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

    // gsKey -> constant alpha (0..1) for fill (/ca) and stroke (/CA); assembled
    // into the page's /ExtGState resource by PdfDocumentBuilder.
    internal Dictionary<string, (double Fill, double Stroke)> ExtGStateAlphas { get; } = new();

    // Open PushClip / PushOpacity scopes, innermost last, with the opacity in
    // force inside each (per-call image opacity multiplies with it).
    private readonly List<(ScopeKind Kind, double Fill, double Stroke)> _scopes = new();

    private enum ScopeKind
    {
        Clip,
        Opacity,
    }

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

            if (run.Script == LipiScript.Latin)
            {
                // Latin: logical-order cmap emit — no shaping required.
                _w.PushState();
                _w.SetFillRgb(color);
                _w.ShowGlyphsAt(FontKey(faceName), size, penX, y, EncodeGlyphs(run.Text, face));
                _w.PopState();
                penX += MeasureLipiRun(run.Text, face, size);
            }
            else
            {
                // Indic: shape the run (reorder + GSUB/GPOS), then emit per
                // glyph inside an /ActualText span carrying the source text,
                // so the run copies and searches as written.
                IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(face.Face, run.Text, run.Script);
                double scale = size / 1000.0;
                _w.BeginActualText(run.Text);
                foreach (ShapedGlyph glyph in glyphs)
                {
                    face.UsedGlyphs.Add(glyph.GlyphId);
                    _w.PushState();
                    _w.SetFillRgb(color);
                    _w.ShowGlyphsAt(
                        FontKey(faceName),
                        size,
                        penX + (glyph.XOffset * scale),
                        y - (glyph.YOffset * scale),
                        GlyphHex(glyph.GlyphId));
                    _w.PopState();
                    penX += glyph.XAdvance * scale;
                }

                _w.EndMarkedContent();
            }
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

    /// <summary>
    /// Draws a pre-shaped glyph run and records the source text, so the run
    /// can be copied, searched and extracted. Each glyph's cluster is the span
    /// of <paramref name="text"/> from <paramref name="clusterStarts"/>[i] to
    /// the next larger start (or the end of the text). A cluster drawn by one
    /// glyph gets a ToUnicode entry; a cluster drawn by several glyphs
    /// (conjuncts, ligature parts, split vowels), or by a glyph that already
    /// stands for other text, is wrapped in a <c>/Span</c> with
    /// <c>/ActualText</c>.
    /// </summary>
    /// <param name="glyphs">The shaped glyphs, in visual order.</param>
    /// <param name="text">The source text the glyphs were shaped from.</param>
    /// <param name="clusterStarts">
    /// For each glyph, the UTF-16 index in <paramref name="text"/> where its
    /// cluster starts (glyphs of one cluster share a start).
    /// </param>
    /// <param name="x">The pen x position (top-left origin).</param>
    /// <param name="y">The baseline-top y position.</param>
    /// <param name="font">The name of a registered custom font.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="color">The fill color.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="clusterStarts"/> does not have one entry per glyph, or an
    /// entry is outside <paramref name="text"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException"><paramref name="font"/> is not a registered custom font.</exception>
    public PageBuilder DrawShapedRun(
        IReadOnlyList<ShapedGlyph> glyphs, string text, IReadOnlyList<int> clusterStarts,
        double x, double y, string font, double size, Color color)
        => DrawShapedRun(glyphs, text, clusterStarts, x, y, font, size, color, DefaultRunStyle);

    /// <summary>
    /// Draws a pre-shaped glyph run with its source text (see
    /// <see cref="DrawShapedRun(IReadOnlyList{ShapedGlyph}, string, IReadOnlyList{int}, double, double, string, double, Color)"/>)
    /// and a <see cref="GlyphRunStyle"/>: horizontal scale, slant and a bold
    /// stroke.
    /// </summary>
    /// <param name="glyphs">The shaped glyphs, in visual order.</param>
    /// <param name="text">The source text the glyphs were shaped from.</param>
    /// <param name="clusterStarts">For each glyph, the UTF-16 index in <paramref name="text"/> where its cluster starts.</param>
    /// <param name="x">The pen x position (top-left origin).</param>
    /// <param name="y">The baseline-top y position.</param>
    /// <param name="font">The name of a registered custom font.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="color">The fill (and bold-stroke) color.</param>
    /// <param name="style">The text-matrix and render-mode options.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="clusterStarts"/> does not match the glyphs, or <paramref name="style"/> is invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException"><paramref name="font"/> is not a registered custom font.</exception>
    public PageBuilder DrawShapedRun(
        IReadOnlyList<ShapedGlyph> glyphs, string text, IReadOnlyList<int> clusterStarts,
        double x, double y, string font, double size, Color color, GlyphRunStyle style)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(clusterStarts);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(style);
        style.Validate();
        if (clusterStarts.Count != glyphs.Count)
        {
            throw new ArgumentException("clusterStarts needs one entry per glyph.", nameof(clusterStarts));
        }

        SortedSet<int> starts = new SortedSet<int>();
        foreach (int start in clusterStarts)
        {
            if (start < 0 || start > text.Length)
            {
                throw new ArgumentException($"Cluster start {start} is outside the text.", nameof(clusterStarts));
            }

            starts.Add(start);
        }

        if (_customFonts is null || !_customFonts.TryGet(font, out CustomFont custom))
        {
            throw new InvalidOperationException(
                $"DrawShapedRun requires a registered custom font; '{font}' is not registered.");
        }

        Fonts.Add(font);
        double scale = size / 1000.0;
        double scaleX = style.HorizontalScale;
        double penX = x;
        HashSet<int> emitted = new HashSet<int>();
        int i = 0;
        while (i < glyphs.Count)
        {
            // One run of consecutive glyphs from the same cluster.
            int start = clusterStarts[i];
            int j = i + 1;
            while (j < glyphs.Count && clusterStarts[j] == start)
            {
                j++;
            }

            string clusterText = text.Substring(start, ClusterEnd(starts, start, text.Length) - start);
            bool first = emitted.Add(start);
            bool plain = first && j - i == 1 && ClaimGlyphText(custom, glyphs[i].GlyphId, clusterText);
            if (!plain)
            {
                // A later run of a cluster already spoken for says nothing.
                _w.BeginActualText(first ? clusterText : string.Empty);
            }

            for (int k = i; k < j; k++)
            {
                ShapedGlyph glyph = glyphs[k];
                custom.UsedGlyphs.Add(glyph.GlyphId);
                _w.PushState();
                _w.SetFillRgb(color);
                if (style.StrokeWidth > 0)
                {
                    _w.SetStrokeRgb(color);
                }

                _w.ShowGlyphsStyled(
                    FontKey(font), size, penX + (glyph.XOffset * scale * scaleX), y - (glyph.YOffset * scale),
                    GlyphHex(glyph.GlyphId), scaleX, style.Skew, style.StrokeWidth);
                _w.PopState();
                penX += glyph.XAdvance * scale * scaleX;
            }

            if (!plain)
            {
                _w.EndMarkedContent();
            }

            i = j;
        }

        return this;
    }

    private static readonly GlyphRunStyle DefaultRunStyle = new GlyphRunStyle();

    private const int MaxToUnicodeText = 256;

    private static int ClusterEnd(SortedSet<int> starts, int start, int textLength)
    {
        foreach (int s in starts.GetViewBetween(start, textLength))
        {
            if (s > start)
            {
                return s;
            }
        }

        return textLength;
    }

    // Gives a single glyph the cluster's text through ToUnicode when that
    // cannot contradict another use of the glyph: the glyph is the cmap glyph
    // of the cluster's one code point and no other code point maps to it, or
    // a glyph no code point reaches whose text is unclaimed or the same.
    // Returns false when /ActualText is needed.
    private static bool ClaimGlyphText(CustomFont custom, int gid, string clusterText)
    {
        // A ToUnicode destination holds at most 512 bytes (256 UTF-16 units);
        // longer clusters (and empty ones) use /ActualText.
        if (gid <= 0 || clusterText.Length == 0 || clusterText.Length > MaxToUnicodeText)
        {
            return false;
        }

        int codepoint = char.ConvertToUtf32(clusterText, 0);
        bool singleCodepoint = clusterText.Length == (codepoint > 0xFFFF ? 2 : 1);
        int cmapCodePoints = custom.CmapCodePointCount(gid);
        if (cmapCodePoints > 0)
        {
            // A glyph two code points share (space and no-break space) could
            // be retold by a later DrawText, so it gets /ActualText instead.
            if (!singleCodepoint || cmapCodePoints != 1 || custom.Face.GetGlyphIndex(codepoint) != gid)
            {
                return false;
            }

            custom.UsedCodepoints.Add(codepoint);
            return true;
        }

        if (custom.GlyphText.TryGetValue(gid, out string? claimed))
        {
            return string.Equals(claimed, clusterText, StringComparison.Ordinal);
        }

        custom.GlyphText[gid] = clusterText;
        return true;
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
    /// Draws an arrow from (x1, y1) to (x2, y2): a stroked shaft plus filled
    /// triangular head(s) in the same colour. The shaft stops at each head's
    /// base so the line cap never pokes through a tip. Head geometry comes from
    /// <see cref="ArrowGeometry"/>, so an on-screen preview built from
    /// <see cref="ArrowGeometry.HeadPoints"/> matches this output exactly.
    /// A zero-length arrow draws nothing.
    /// </summary>
    /// <param name="x1">Start x (top-left page coordinates).</param>
    /// <param name="y1">Start y (top-left page coordinates).</param>
    /// <param name="x2">End x (top-left page coordinates).</param>
    /// <param name="y2">End y (top-left page coordinates).</param>
    /// <param name="color">Shaft and head colour.</param>
    /// <param name="width">Shaft stroke width.</param>
    /// <param name="ends">Which ends carry a head.</param>
    /// <param name="headLength">
    /// Head length, or null for <see cref="ArrowGeometry.DefaultHeadLength"/>.
    /// </param>
    /// <param name="headWidth">
    /// Head base width, or null for <see cref="ArrowGeometry.DefaultHeadWidth"/>.
    /// </param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a head size is negative or not finite.
    /// </exception>
    public PageBuilder DrawArrow(
        double x1,
        double y1,
        double x2,
        double y2,
        Color color,
        double width = 1.0,
        ArrowEnds ends = ArrowEnds.End,
        double? headLength = null,
        double? headWidth = null)
    {
        double length = headLength ?? ArrowGeometry.DefaultHeadLength(width);
        double baseWidth = headWidth ?? ArrowGeometry.DefaultHeadWidth(length);

        PointF start = new PointF(x1, y1);
        PointF end = new PointF(x2, y2);
        PointF[] endHead = ends != ArrowEnds.Start
            ? ArrowGeometry.HeadPoints(start, end, length, baseWidth)
            : Array.Empty<PointF>();
        PointF[] startHead = ends != ArrowEnds.End
            ? ArrowGeometry.HeadPoints(end, start, length, baseWidth)
            : Array.Empty<PointF>();

        if (start.X == end.X && start.Y == end.Y)
        {
            return this;
        }

        // A double-headed arrow shares the shaft between both heads, so each
        // head may claim at most half of it.
        double shaftLength = start.DistanceTo(end);
        double perHead = ends == ArrowEnds.Both ? Math.Min(length, shaftLength / 2.0) : length;
        if (ends == ArrowEnds.Both && perHead < length)
        {
            double scaledWidth = baseWidth * (perHead / length);
            endHead = ArrowGeometry.HeadPoints(start, end, perHead, scaledWidth);
            startHead = ArrowGeometry.HeadPoints(end, start, perHead, scaledWidth);
        }

        PointF shaftStart = startHead.Length > 0 ? ArrowGeometry.ShaftEnd(end, start, perHead) : start;
        PointF shaftEnd = endHead.Length > 0 ? ArrowGeometry.ShaftEnd(start, end, perHead) : end;

        _w.PushState();
        _w.SetStrokeRgb(color);
        _w.SetFillRgb(color);
        _w.SetLineWidth(width);

        if (shaftStart.DistanceTo(shaftEnd) > 0.0)
        {
            _w.MoveToTopLeft(shaftStart.X, shaftStart.Y);
            _w.LineToTopLeft(shaftEnd.X, shaftEnd.Y);
            _w.Stroke();
        }

        FillTriangle(endHead);
        FillTriangle(startHead);
        _w.PopState();
        return this;
    }

    private void FillTriangle(PointF[] points)
    {
        if (points.Length != 3)
        {
            return;
        }

        _w.MoveToTopLeft(points[0].X, points[0].Y);
        _w.LineToTopLeft(points[1].X, points[1].Y);
        _w.LineToTopLeft(points[2].X, points[2].Y);
        _w.ClosePath();
        _w.Fill();
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

        WritePath(path);
        PaintPath(fill is not null, stroke is not null, fillRule);
        _w.PopState();
        return this;
    }

    /// <summary>
    /// Draws a <see cref="Path"/> with a <see cref="LineStyle"/> for the
    /// outline (width, dashes, caps, joins). Supply at least one of
    /// <paramref name="fill"/> or <paramref name="stroke"/>; a path with
    /// neither, or an empty path, paints nothing.
    /// </summary>
    /// <param name="path">The path, in top-left coordinates.</param>
    /// <param name="fill">The fill colour, or null for no fill.</param>
    /// <param name="stroke">The outline colour, or null for no outline.</param>
    /// <param name="style">How the outline is stroked.</param>
    /// <param name="fillRule">Non-zero winding (default) or even-odd filling.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="style"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="style"/> is not valid.</exception>
    public PageBuilder DrawPath(Path path, Color? fill, Color? stroke, LineStyle style, FillRule fillRule = FillRule.NonZeroWinding)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(style);
        style.Validate();
        if (path.IsEmpty || (fill is null && stroke is null)) { return this; }

        _w.PushState();
        if (fill is Color f) { _w.SetFillRgb(f); }
        if (stroke is Color s)
        {
            _w.SetStrokeRgb(s);
            ApplyLineStyle(style);
        }

        WritePath(path);
        PaintPath(fill is not null, stroke is not null, fillRule);
        _w.PopState();
        return this;
    }

    /// <summary>Draws a line from (x1, y1) to (x2, y2) with a <see cref="LineStyle"/> (dashes, caps).</summary>
    /// <param name="x1">Start x (top-left origin).</param>
    /// <param name="y1">Start y.</param>
    /// <param name="x2">End x.</param>
    /// <param name="y2">End y.</param>
    /// <param name="color">The line colour.</param>
    /// <param name="style">How the line is stroked.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="style"/> is not valid.</exception>
    public PageBuilder DrawLine(double x1, double y1, double x2, double y2, Color color, LineStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.Validate();
        _w.PushState();
        _w.SetStrokeRgb(color);
        ApplyLineStyle(style);
        _w.MoveToTopLeft(x1, y1);
        _w.LineToTopLeft(x2, y2);
        _w.Stroke();
        _w.PopState();
        return this;
    }

    /// <summary>
    /// Draws a rectangle with a <see cref="LineStyle"/> for the outline. Supply
    /// at least one of <paramref name="fill"/> or <paramref name="stroke"/>.
    /// </summary>
    /// <param name="x">Left edge (top-left origin).</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    /// <param name="fill">The fill colour, or null for no fill.</param>
    /// <param name="stroke">The outline colour, or null for no outline.</param>
    /// <param name="style">How the outline is stroked.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="style"/> is not valid.</exception>
    public PageBuilder DrawRectangle(double x, double y, double width, double height, Color? fill, Color? stroke, LineStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.Validate();
        if (fill is null && stroke is null) { return this; }

        _w.PushState();
        if (fill is Color f) { _w.SetFillRgb(f); }
        if (stroke is Color s)
        {
            _w.SetStrokeRgb(s);
            ApplyLineStyle(style);
        }

        _w.RectTopLeft(x, y, width, height);
        PaintPath(fill is not null, stroke is not null, FillRule.NonZeroWinding);
        _w.PopState();
        return this;
    }

    private void ApplyLineStyle(LineStyle style)
    {
        _w.SetLineWidth(style.Width);
        if (style.DashPattern.Count > 0)
        {
            _w.SetDash(style.DashPattern, style.DashPhase);
        }

        if (style.Cap != LineCap.Butt)
        {
            _w.SetLineCap((int)style.Cap);
        }

        if (style.Join != LineJoin.Miter)
        {
            _w.SetLineJoin((int)style.Join);
        }

        if (style.MiterLimit != 10.0)
        {
            _w.SetMiterLimit(style.MiterLimit);
        }
    }

    private void WritePath(Path path)
    {
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
    }

    private void PaintPath(bool fill, bool stroke, FillRule fillRule)
    {
        bool evenOdd = fillRule == FillRule.EvenOdd;
        if (fill && stroke)
        {
            if (evenOdd) { _w.FillAndStrokeEvenOdd(); } else { _w.FillAndStroke(); }
        }
        else if (fill)
        {
            if (evenOdd) { _w.FillEvenOdd(); } else { _w.Fill(); }
        }
        else
        {
            _w.Stroke();
        }
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
        _w.DrawImage(key, x, y, width, height, RegisterAlpha(opacity * CurrentFillOpacity, opacity * CurrentStrokeOpacity));
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
        _w.DrawImage(key, x, y, width, height, RegisterAlpha(opacity * CurrentFillOpacity, opacity * CurrentStrokeOpacity));
        return this;
    }

    /// <summary>
    /// Embeds an image and draws the <paramref name="crop"/> part of it into
    /// the given top-left rectangle. The crop is done by clipping, so the
    /// image is embedded whole (a JPEG stays untouched) and the cropped-away
    /// pixels remain in the file — do not use cropping to hide content.
    /// </summary>
    /// <param name="imageBytes">The encoded image (JPEG, PNG, TIFF or BMP).</param>
    /// <param name="x">Left edge of the destination (top-left origin).</param>
    /// <param name="y">Top edge of the destination.</param>
    /// <param name="width">Destination width in points.</param>
    /// <param name="height">Destination height in points.</param>
    /// <param name="crop">The part of the image to show, in source pixels.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="imageBytes"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The crop has no area or is not finite.</exception>
    /// <exception cref="ArgumentException">The bytes are not a supported image.</exception>
    public PageBuilder DrawImage(byte[] imageBytes, double x, double y, double width, double height, ImageCrop crop)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ValidateCrop(crop);
        EmbeddedImage built = ImageEmbedder.Build(imageBytes);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, imageBytes, null) { Built = built });
        DrawCropped(key, built.Width, built.Height, x, y, width, height, crop);
        return this;
    }

    /// <summary>
    /// Embeds a decoded image frame and draws the <paramref name="crop"/> part
    /// of it into the given top-left rectangle (by clipping; see
    /// <see cref="DrawImage(byte[], double, double, double, double, ImageCrop)"/>).
    /// </summary>
    /// <param name="image">The decoded frame.</param>
    /// <param name="x">Left edge of the destination (top-left origin).</param>
    /// <param name="y">Top edge of the destination.</param>
    /// <param name="width">Destination width in points.</param>
    /// <param name="height">Destination height in points.</param>
    /// <param name="crop">The part of the image to show, in source pixels.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The crop has no area or is not finite.</exception>
    public PageBuilder DrawImage(ImageFrame image, double x, double y, double width, double height, ImageCrop crop)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateCrop(crop);
        string key = $"Img{Images.Count}";
        Images.Add(new ImageRef(key, null, image));
        DrawCropped(key, image.Width, image.Height, x, y, width, height, crop);
        return this;
    }

    private static void ValidateCrop(ImageCrop crop)
    {
        if (!double.IsFinite(crop.X) || !double.IsFinite(crop.Y)
            || !double.IsFinite(crop.Width) || !double.IsFinite(crop.Height)
            || crop.Width <= 0 || crop.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), crop, "The crop must be finite with a positive width and height.");
        }
    }

    // Clips to the destination and places the whole image so that the crop
    // rectangle fills it.
    private void DrawCropped(string key, int imageWidth, int imageHeight, double x, double y, double width, double height, ImageCrop crop)
    {
        double sx = width / crop.Width;
        double sy = height / crop.Height;
        _w.PushState();
        _w.RectTopLeft(x, y, width, height);
        _w.ClipAndEndPath(evenOdd: false);
        _w.DrawImage(key, x - (crop.X * sx), y - (crop.Y * sy), imageWidth * sx, imageHeight * sy);
        _w.PopState();
    }

    // ── Clipping, opacity and links ───────────────────────────────────────

    /// <summary>
    /// Starts clipping to a rectangle: everything drawn until the matching
    /// <see cref="PopClip"/> is cut off outside it (and outside any clip
    /// already in force). Emits <c>q … re W n</c>.
    /// </summary>
    /// <param name="x">Left edge (top-left origin).</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    /// <returns>This page builder.</returns>
    public PageBuilder PushClip(double x, double y, double width, double height)
    {
        _w.PushState();
        _w.RectTopLeft(x, y, width, height);
        _w.ClipAndEndPath(evenOdd: false);
        _scopes.Add((ScopeKind.Clip, CurrentFillOpacity, CurrentStrokeOpacity));
        return this;
    }

    /// <summary>
    /// Starts clipping to a path (top-left coordinates), until the matching
    /// <see cref="PopClip"/>.
    /// </summary>
    /// <param name="path">The clip outline. An empty path clips everything away.</param>
    /// <param name="fillRule">Which points are inside: non-zero winding (default) or even-odd.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public PageBuilder PushClip(Path path, FillRule fillRule = FillRule.NonZeroWinding)
    {
        ArgumentNullException.ThrowIfNull(path);
        _w.PushState();
        if (path.IsEmpty)
        {
            _w.RectTopLeft(0, 0, 0, 0);
        }
        else
        {
            WritePath(path);
        }

        _w.ClipAndEndPath(fillRule == FillRule.EvenOdd);
        _scopes.Add((ScopeKind.Clip, CurrentFillOpacity, CurrentStrokeOpacity));
        return this;
    }

    /// <summary>Ends the innermost clip started with <c>PushClip</c> (emits <c>Q</c>).</summary>
    /// <returns>This page builder.</returns>
    /// <exception cref="InvalidOperationException">The innermost open scope is not a clip.</exception>
    public PageBuilder PopClip() => PopScope(ScopeKind.Clip, nameof(PopClip));

    /// <summary>
    /// Draws everything until the matching <see cref="PopOpacity"/> with the
    /// given constant opacity (fills and strokes alike; text, paths and
    /// images). Nested scopes multiply.
    /// </summary>
    /// <param name="opacity">0 (invisible) to 1 (opaque).</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="opacity"/> is outside [0, 1].</exception>
    public PageBuilder PushOpacity(double opacity) => PushOpacity(opacity, opacity);

    /// <summary>
    /// Draws everything until the matching <see cref="PopOpacity"/> with the
    /// given fill and stroke opacity (ExtGState <c>/ca</c> and <c>/CA</c>).
    /// Nested scopes multiply; an image's own opacity multiplies too.
    /// </summary>
    /// <param name="fillOpacity">Opacity of fills and text, 0 to 1.</param>
    /// <param name="strokeOpacity">Opacity of strokes, 0 to 1.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An opacity is outside [0, 1].</exception>
    public PageBuilder PushOpacity(double fillOpacity, double strokeOpacity)
    {
        ValidateOpacity(fillOpacity);
        ValidateOpacity(strokeOpacity);
        double fill = CurrentFillOpacity * fillOpacity;
        double stroke = CurrentStrokeOpacity * strokeOpacity;
        _w.PushState();
        _w.SetExtGState(RegisterAlpha(fill, stroke));
        _scopes.Add((ScopeKind.Opacity, fill, stroke));
        return this;
    }

    /// <summary>Ends the innermost <c>PushOpacity</c> scope (emits <c>Q</c>).</summary>
    /// <returns>This page builder.</returns>
    /// <exception cref="InvalidOperationException">The innermost open scope is not an opacity scope.</exception>
    public PageBuilder PopOpacity() => PopScope(ScopeKind.Opacity, nameof(PopOpacity));

    /// <summary>
    /// Adds a clickable link to <paramref name="uri"/> over a rectangle. Nothing
    /// is drawn; the link has no border.
    /// </summary>
    /// <param name="x">Left edge (top-left origin).</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    /// <param name="uri">The link target, written as given.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    public PageBuilder AddLink(double x, double y, double width, double height, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Hyperlinks.Add(new HyperlinkRect(x, _w.FlipY(y + height), width, height, uri.OriginalString));
        return this;
    }

    /// <summary>
    /// Adds a link over a rectangle that jumps to another page of this
    /// document (a <c>/GoTo</c> action), showing <paramref name="targetTop"/>
    /// at the top of the window. Nothing is drawn.
    /// </summary>
    /// <param name="x">Left edge (top-left origin).</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in points.</param>
    /// <param name="height">Height in points.</param>
    /// <param name="targetPageIndex">The zero-based index of the target page, checked when the document is saved.</param>
    /// <param name="targetTop">The y position on the target page (top-left origin) to scroll to.</param>
    /// <returns>This page builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetPageIndex"/> is negative.</exception>
    public PageBuilder AddLink(double x, double y, double width, double height, int targetPageIndex, double targetTop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetPageIndex);
        Hyperlinks.Add(new HyperlinkRect(x, _w.FlipY(y + height), width, height, null, targetPageIndex, targetTop));
        return this;
    }

    // Closes every open PushClip / PushOpacity scope (at save time).
    internal void CloseOpenScopes()
    {
        while (_scopes.Count > 0)
        {
            _scopes.RemoveAt(_scopes.Count - 1);
            _w.PopState();
        }
    }

    private double CurrentFillOpacity => _scopes.Count == 0 ? 1.0 : _scopes[^1].Fill;

    private double CurrentStrokeOpacity => _scopes.Count == 0 ? 1.0 : _scopes[^1].Stroke;

    private PageBuilder PopScope(ScopeKind kind, string method)
    {
        if (_scopes.Count == 0 || _scopes[^1].Kind != kind)
        {
            string open = _scopes.Count == 0 ? "nothing is open" : $"the innermost open scope is {(_scopes[^1].Kind == ScopeKind.Clip ? "a clip" : "an opacity scope")}";
            throw new InvalidOperationException($"{method} does not match: {open}.");
        }

        _scopes.RemoveAt(_scopes.Count - 1);
        _w.PopState();
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
    private string RegisterAlpha(double fill, double stroke)
    {
        foreach (KeyValuePair<string, (double Fill, double Stroke)> existing in ExtGStateAlphas)
        {
            if (existing.Value.Fill == fill && existing.Value.Stroke == stroke)
            {
                return existing.Key;
            }
        }

        string key = $"GsA{ExtGStateAlphas.Count}";
        ExtGStateAlphas[key] = (fill, stroke);
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
internal sealed record ImageRef(string Key, byte[]? Bytes, ImageFrame? Frame)
{
    /// <summary>Gets the embedded form when it was built at draw time (crops need the size).</summary>
    internal EmbeddedImage? Built { get; init; }
}
