// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — glyph-level text positioning

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Graphics;

// Type aliases force binding to Chuvadi.Pdf.Text even if Phase 2.1
// scaffolding ever reintroduces shadowing types in the enclosing
// Chuvadi.Pdf.Rendering.DisplayList namespace. C# name resolution
// normally prefers the enclosing namespace; aliases override that.
using TextRun = Chuvadi.Pdf.Text.TextRun;
using GlyphPosition = Chuvadi.Pdf.Text.GlyphPosition;
using TextDirection = Chuvadi.Pdf.Text.TextDirection;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Walks a <see cref="PageDisplayList"/> and produces a sequence of
/// <see cref="TextRun"/>s in reading order.
/// </summary>
/// <remarks>
/// <para>
/// Reading-order detection in v1: cluster runs into baseline-grouped
/// lines, sort lines top-to-bottom, sort runs within a line by
/// X-position. Adequate for single-column layouts; multi-column flows
/// are a Phase 2.2 concern.
/// </para>
/// <para>
/// <b>Builder status (v2.0.0):</b> the current
/// <see cref="DisplayListBuilder"/> emits per-glyph <see cref="DrawGlyphOp"/>s,
/// not grouped <see cref="TextOp"/>s. <see cref="Extract"/> therefore
/// returns an empty list for any display list built by the shipping
/// v2.0.0 pipeline. The extractor is wired in advance so that the
/// Phase 2.1 builder pass — which will emit <see cref="TextOp"/>s
/// — can produce reading-order text without further public-API
/// changes. For text extraction in v2.0.0, use
/// <see cref="Chuvadi.Pdf.Text.PdfDocumentTextExtensions.GetTextRuns"/>,
/// which operates on the content stream directly.
/// </para>
/// </remarks>
public static class TextRunExtractor
{
    /// <summary>Extracts text runs from a page's display list.</summary>
    /// <param name="list">The page display list to walk.</param>
    /// <returns>
    /// Text runs in reading order, or an empty list when the display
    /// list contains no <see cref="TextOp"/> entries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="list"/> is null.
    /// </exception>
    public static IReadOnlyList<TextRun> Extract(PageDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        List<RawRun> raw = new List<RawRun>();

        foreach (RenderOp op in list.Ops)
        {
            if (op is TextOp t)
            {
                raw.Add(BuildRawRun(t));
            }
        }

        if (raw.Count == 0)
        {
            return Array.Empty<TextRun>();
        }

        // Reading-order detection: group by baseline (Y), sort lines
        // top-to-bottom, runs left-to-right within each line.
        raw.Sort(CompareByReadingOrder);

        List<TextRun> runs = new List<TextRun>(raw.Count);

        for (int i = 0; i < raw.Count; i++)
        {
            RawRun r = raw[i];

            runs.Add(new TextRun(
                unicode: r.Unicode,
                boundingBox: r.Bounds,
                fontSize: r.FontSize,
                direction: TextDirection.LeftToRight,
                glyphs: r.Glyphs,
                readingOrderIndex: i));
        }

        return runs;
    }

    private static int CompareByReadingOrder(RawRun a, RawRun b)
    {
        // Baseline distance threshold: half a line height counts as same line.
        double lineThreshold = Math.Max(a.FontSize, b.FontSize) * 0.5;

        if (Math.Abs(a.BaselineY - b.BaselineY) < lineThreshold)
        {
            return a.OriginX.CompareTo(b.OriginX);
        }

        // PDF Y goes up — higher Y is higher on the page, so it comes
        // first in reading order.
        return b.BaselineY.CompareTo(a.BaselineY);
    }

    private static RawRun BuildRawRun(TextOp t)
    {
        StringBuilder sb = new StringBuilder();
        List<GlyphPosition> positions = new List<GlyphPosition>(t.Glyphs.Count);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        for (int i = 0; i < t.Glyphs.Count; i++)
        {
            DisplayListGlyph g = t.Glyphs[i];

            sb.Append(g.Unicode);

            PointF originWorld = t.Transform.TransformPoint(new PointF(g.X, g.Y));
            PointF advanceEndWorld = t.Transform.TransformPoint(
                new PointF(g.X + g.Advance, g.Y));
            double advanceWorld = advanceEndWorld.X - originWorld.X;

            int unicodeCodePoint = FirstCodePoint(g.Unicode);

            positions.Add(new GlyphPosition(
                originWorld.X,
                originWorld.Y,
                advanceWorld,
                unicodeCodePoint));

            // Approximate cell height from the transform's vertical scale.
            double cellHeight = t.FontSize *
                Math.Max(Math.Abs(t.Transform.D), Math.Abs(t.Transform.A));

            double cellLeft = originWorld.X;
            double cellBottom = originWorld.Y;
            double cellRight = originWorld.X + advanceWorld;
            double cellTop = originWorld.Y + cellHeight;

            if (cellLeft < minX) { minX = cellLeft; }
            if (cellBottom < minY) { minY = cellBottom; }
            if (cellRight > maxX) { maxX = cellRight; }
            if (cellTop > maxY) { maxY = cellTop; }
        }

        // Empty-glyph guard — when the op carries no glyphs, fall back to
        // a zero-extent bounds at the transform's origin so downstream
        // code never sees infinities.
        if (t.Glyphs.Count == 0)
        {
            PointF zero = t.Transform.TransformPoint(PointF.Zero);
            minX = zero.X;
            minY = zero.Y;
            maxX = zero.X;
            maxY = zero.Y;
        }

        RectangleF bounds = new RectangleF(
            (float)minX,
            (float)minY,
            (float)(maxX - minX),
            (float)(maxY - minY));

        PointF origin = t.Transform.TransformPoint(PointF.Zero);

        return new RawRun
        {
            Unicode = sb.ToString(),
            Bounds = bounds,
            Glyphs = positions,
            OriginX = origin.X,
            BaselineY = origin.Y,
            FontSize = t.FontSize,
        };
    }

    /// <summary>
    /// Returns the first Unicode code point of <paramref name="s"/>,
    /// handling surrogate pairs. Returns 0 for the empty string.
    /// </summary>
    private static int FirstCodePoint(string s)
    {
        if (s.Length == 0)
        {
            return 0;
        }

        if (char.IsHighSurrogate(s[0]) && s.Length > 1 && char.IsLowSurrogate(s[1]))
        {
            return char.ConvertToUtf32(s[0], s[1]);
        }

        return s[0];
    }

    private sealed class RawRun
    {
        internal string Unicode { get; init; } = string.Empty;

        internal RectangleF Bounds { get; init; }

        internal IReadOnlyList<GlyphPosition> Glyphs { get; init; } =
            Array.Empty<GlyphPosition>();

        internal double OriginX { get; init; }

        internal double BaselineY { get; init; }

        internal double FontSize { get; init; }
    }
}
