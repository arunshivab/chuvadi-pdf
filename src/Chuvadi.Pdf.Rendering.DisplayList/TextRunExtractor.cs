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

        // Accumulate the run's extent in TEXT space (axis-aligned there), then
        // transform the whole box to page space so rotation / shear produce a
        // correct page-space AABB. The earlier approach assumed a horizontal
        // advance and a vertical em, collapsing the width to zero for rotated
        // (e.g. vertical) text.
        double tsMinX = double.MaxValue, tsMinY = double.MaxValue;
        double tsMaxX = double.MinValue, tsMaxY = double.MinValue;

        foreach (DisplayListGlyph g in t.Glyphs)
        {
            sb.Append(g.Unicode);
            (double wx, double wy) = t.Transform.Apply(g.X, g.Y);
            (double wxa, double wya) = t.Transform.Apply(g.X + g.Advance, g.Y);
            double advanceWorld =
                Math.Sqrt(((wxa - wx) * (wxa - wx)) + ((wya - wy) * (wya - wy)));

            positions.Add(new GlyphPosition(wx, wy, advanceWorld, g.Unicode));

            // Glyph cell in text space: baseline origin to advance (x), baseline
            // to ascent ~ one em (y).
            double gx1 = g.X + g.Advance;
            double gy1 = g.Y + t.FontSize;
            if (g.X < tsMinX) { tsMinX = g.X; }
            if (gx1 < tsMinX) { tsMinX = gx1; }
            if (g.X > tsMaxX) { tsMaxX = g.X; }
            if (gx1 > tsMaxX) { tsMaxX = gx1; }
            if (g.Y < tsMinY) { tsMinY = g.Y; }
            if (gy1 < tsMinY) { tsMinY = gy1; }
            if (g.Y > tsMaxY) { tsMaxY = g.Y; }
            if (gy1 > tsMaxY) { tsMaxY = gy1; }
        }

        Rect bounds = TransformedBounds(t.Transform, t.Glyphs.Count, tsMinX, tsMinY, tsMaxX, tsMaxY);
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

    // Maps a text-space axis-aligned box through the run transform and returns
    // the page-space axis-aligned bounding box of its four corners. Handles
    // rotation and shear; collapses to a zero-size box at the origin for an
    // empty run.
    private static Rect TransformedBounds(
        AffineMatrix m, int glyphCount, double minX, double minY, double maxX, double maxY)
    {
        if (glyphCount == 0)
        {
            (double ox, double oy) = m.Apply(0, 0);
            return new Rect(ox, oy, 0, 0);
        }

        (double ax, double ay) = m.Apply(minX, minY);
        (double bx, double by) = m.Apply(maxX, minY);
        (double cx, double cy) = m.Apply(maxX, maxY);
        (double dx, double dy) = m.Apply(minX, maxY);

        double pMinX = Math.Min(Math.Min(ax, bx), Math.Min(cx, dx));
        double pMinY = Math.Min(Math.Min(ay, by), Math.Min(cy, dy));
        double pMaxX = Math.Max(Math.Max(ax, bx), Math.Max(cx, dx));
        double pMaxY = Math.Max(Math.Max(ay, by), Math.Max(cy, dy));
        return new Rect(pMinX, pMinY, pMaxX - pMinX, pMaxY - pMinY);
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
