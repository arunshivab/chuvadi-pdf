// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — glyph-level text positioning

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Walks a <see cref="PageDisplayList"/> and produces a sequence of
/// <see cref="TextRun"/>s in reading order.
/// </summary>
/// <remarks>
/// Reading-order detection in v1: cluster runs into baseline-grouped lines,
/// sort lines top-to-bottom, sort runs within a line by x-position. Adequate
/// for single-column layouts; multi-column flows are a Phase 2.2 concern.
/// </remarks>
public static class TextRunExtractor
{
    /// <summary>Extracts text runs from a page's display list.</summary>
    public static IReadOnlyList<TextRun> Extract(PageDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        List<RawRun> raw = new();
        foreach (RenderOp op in list)
        {
            if (op is TextOp t) { raw.Add(BuildRawRun(t)); }
        }

        // Reading-order detection: group by baseline (Y), sort lines T→B, runs L→R.
        raw.Sort((a, b) =>
        {
            // Baseline distance threshold: half a line height counts as same line.
            double lineThreshold = Math.Max(a.FontSize, b.FontSize) * 0.5;
            if (Math.Abs(a.BaselineY - b.BaselineY) < lineThreshold)
            {
                return a.OriginX.CompareTo(b.OriginX);
            }
            // PDF Y goes UP, so higher Y is higher on the page → earlier in reading.
            return b.BaselineY.CompareTo(a.BaselineY);
        });

        List<TextRun> runs = new(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            RawRun r = raw[i];
            runs.Add(new TextRun(
                unicode: r.Unicode,
                boundingBox: r.Bounds,
                glyphs: r.Glyphs,
                direction: TextDirection.LeftToRight,
                readingOrderIndex: i,
                fontFamily: r.Style.FontFamily,
                fontWeight: r.Style.Weight,
                slant: r.Style.Slant,
                fontSize: r.FontSize,
                layers: r.Layers));
        }
        return runs;
    }

    private static RawRun BuildRawRun(TextOp t)
    {
        System.Text.StringBuilder sb = new();
        List<GlyphPosition> positions = new(t.Glyphs.Count);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (DisplayListGlyph g in t.Glyphs)
        {
            sb.Append(g.Unicode);
            (double wx, double wy) = t.Transform.Apply(g.X, g.Y);
            (double wxa, _) = t.Transform.Apply(g.X + g.Advance, g.Y);
            double advanceWorld = wxa - wx;

            positions.Add(new GlyphPosition(wx, wy, advanceWorld, g.Unicode));

            double cx = wx;
            double cy = wy;
            double cw = advanceWorld;
            double ch = t.FontSize * Math.Max(Math.Abs(t.Transform.D), Math.Abs(t.Transform.A));

            if (cx < minX) { minX = cx; }
            if (cy < minY) { minY = cy; }
            if (cx + cw > maxX) { maxX = cx + cw; }
            if (cy + ch > maxY) { maxY = cy + ch; }
        }
        Rect bounds = new(minX, minY, maxX - minX, maxY - minY);
        (double bx, double by) = t.Transform.Apply(0, 0);

        return new RawRun
        {
            Unicode = sb.ToString(),
            Bounds = bounds,
            Glyphs = positions,
            OriginX = bx,
            BaselineY = by,
            FontSize = t.FontSize,
            Style = t.Style,
            Layers = t.Layers,
        };
    }

    private sealed class RawRun
    {
        public string Unicode { get; init; } = "";
        public Rect Bounds { get; init; }
        public IReadOnlyList<GlyphPosition> Glyphs { get; init; } = Array.Empty<GlyphPosition>();
        public double OriginX { get; init; }
        public double BaselineY { get; init; }
        public double FontSize { get; init; }

        public FontStyle Style { get; init; } = FontStyle.Default;

        public IReadOnlyList<string> Layers { get; init; } = Array.Empty<string>();
    }
}
