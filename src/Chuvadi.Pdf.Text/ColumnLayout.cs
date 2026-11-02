// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — column detection for layout-aware extraction (D-104)

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Finds bands and columns on a page and puts lines in reading order by them.
/// </summary>
/// <remarks>
/// Lines are taken from the top of the page down. Consecutive lines that share
/// a vertical strip of white space (a gutter) form a band; a line that crosses
/// the strip (a title, a footer, a footnote) ends the band, so it is read after
/// the columns above it. A band is read column by column only when every
/// column looks like running text (wide enough, its lines mostly filling it,
/// long enough); when that test is unsure, the band is read as rows, because
/// splitting a table into columns is worse than not splitting real columns.
/// </remarks>
internal static class ColumnLayout
{
    // A gutter is at least this many times the page's median text size wide.
    private const double GutterEm = 1.0;

    // Columns: at most 3 per band, each at least this many ems wide.
    private const int MaxColumns = 3;
    private const double MinColumnEm = 10.0;

    // Running text: at least half the lines fill 70% of the column, and the
    // median line has at least 20 characters.
    private const double FillRatio = 0.7;
    private const double FillShare = 0.5;
    private const int MinMedianChars = 20;

    // A line starting further than this share of the column's width from its
    // left edge is not a line of running text.
    private const double IndentShare = 0.25;

    // Lines within this many degrees of horizontal take part.
    private const double HorizontalTolerance = 2.0;

    /// <summary>Finds the page's bands and columns; <see cref="PageLayout.Rows"/> when it has no columns.</summary>
    internal static PageLayout Detect(List<List<TextFragment>> lines)
    {
        List<LineBox> boxes = new List<LineBox>();
        foreach (List<TextFragment> line in lines)
        {
            if (IsHorizontal(line[0]))
            {
                boxes.Add(new LineBox(line));
            }
        }

        if (boxes.Count < 4)
        {
            return PageLayout.Rows;
        }

        boxes.Sort((a, b) => b.Y.CompareTo(a.Y));
        double size = MedianSize(boxes);
        double minGutter = Math.Max(4.0, GutterEm * size);
        double left = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        foreach (LineBox b in boxes)
        {
            b.Cover = Coverage(b.Fragments, minGutter);
            left = Math.Min(left, b.X0);
            right = Math.Max(right, b.X1);
        }

        List<Band> bands = new List<Band>();
        List<LineBox> current = new List<LineBox> { boxes[0] };
        List<(double Lo, double Hi)> common = Interior(Free(boxes[0], left, right), left, right);
        for (int i = 1; i < boxes.Count; i++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            List<(double Lo, double Hi)> shared = Interior(Intersect(common, Free(boxes[i], left, right), minGutter), left, right);
            if (shared.Count > 0)
            {
                current.Add(boxes[i]);
                common = shared;
            }
            else
            {
                bands.Add(Close(current, common, size));
                current = new List<LineBox> { boxes[i] };
                common = Interior(Free(boxes[i], left, right), left, right);
            }
        }

        bands.Add(Close(current, common, size));

        // Consecutive bands read as rows become one band.
        List<Band> merged = new List<Band>();
        foreach (Band band in bands)
        {
            if (band.Columns is null && merged.Count > 0 && merged[^1].Columns is null)
            {
                merged[^1].Lines.AddRange(band.Lines);
            }
            else
            {
                merged.Add(band);
            }
        }

        bool anyColumns = false;
        List<LayoutBand> result = new List<LayoutBand>(merged.Count);
        for (int k = 0; k < merged.Count; k++)
        {
            // Bands meet halfway between the last baseline of one and the
            // first baseline of the next, so their rectangles do not overlap.
            Band band = merged[k];
            double top = k == 0 ? band.Top : (merged[k - 1].Bottom + band.TopBaseline) / 2.0;
            double bottom = k == merged.Count - 1 ? band.BottomEdge : (band.Bottom + merged[k + 1].TopBaseline) / 2.0;
            (double X0, double X1) extent = band.Extent;
            List<LayoutColumn> columns = new List<LayoutColumn>();
            if (band.Columns is null)
            {
                columns.Add(new LayoutColumn(new PdfRectangle(extent.X0, bottom, extent.X1, top)));
            }
            else
            {
                anyColumns = true;
                foreach ((double X0, double X1) column in band.Columns)
                {
                    columns.Add(new LayoutColumn(new PdfRectangle(column.X0, bottom, column.X1, top)));
                }
            }

            result.Add(new LayoutBand(new PdfRectangle(extent.X0, bottom, extent.X1, top), columns));
        }

        return anyColumns ? new PageLayout(ReadingOrderSource.Columns, result) : PageLayout.Rows;
    }

    /// <summary>
    /// Puts lines in reading order by <paramref name="layout"/>: each line in
    /// the result holds fragments of one direction, sorted along its baseline.
    /// </summary>
    internal static List<List<TextFragment>> Arrange(List<List<TextFragment>> lines, PageLayout layout)
    {
        if (layout.Source != ReadingOrderSource.Columns || layout.Bands.Count == 0)
        {
            return RowOrder(lines);
        }

        IReadOnlyList<LayoutBand> bands = layout.Bands;

        // Per band, per column: the segments of each line that fall in it.
        List<List<List<TextFragment>>>[] placed = new List<List<List<TextFragment>>>[bands.Count];
        for (int b = 0; b < bands.Count; b++)
        {
            placed[b] = new List<List<List<TextFragment>>>();
            for (int c = 0; c < Math.Max(1, bands[b].Columns.Count); c++)
            {
                placed[b].Add(new List<List<TextFragment>>());
            }
        }

        List<List<TextFragment>> loose = new List<List<TextFragment>>();
        foreach (List<TextFragment> line in lines)
        {
            if (!IsHorizontal(line[0]))
            {
                loose.Add(line);
                continue;
            }

            // A line belongs to the band holding most of its characters, so a
            // raised footnote mark stays with its footnote.
            int band = LineBand(bands, line);
            if (band < 0)
            {
                loose.Add(line);
                continue;
            }

            Dictionary<int, List<TextFragment>> parts = new Dictionary<int, List<TextFragment>>();
            foreach (TextFragment f in line)
            {
                int column = ColumnOf(bands[band], f);
                if (!parts.TryGetValue(column, out List<TextFragment>? part))
                {
                    part = new List<TextFragment>();
                    parts[column] = part;
                }

                part.Add(f);
            }

            foreach (KeyValuePair<int, List<TextFragment>> part in parts)
            {
                placed[band][part.Key].Add(part.Value);
            }
        }

        loose.Sort((a, b) => b[0].Y.CompareTo(a[0].Y));
        List<List<TextFragment>> ordered = new List<List<TextFragment>>();
        int nextLoose = 0;
        for (int b = 0; b < bands.Count; b++)
        {
            double top = Math.Max(bands[b].Bounds.Y1, bands[b].Bounds.Y2);
            while (nextLoose < loose.Count && loose[nextLoose][0].Y > top)
            {
                ordered.Add(SortAlong(loose[nextLoose++]));
            }

            foreach (List<List<TextFragment>> column in placed[b])
            {
                column.Sort((x, y) => y[0].Y.CompareTo(x[0].Y));
                foreach (List<TextFragment> segment in column)
                {
                    ordered.Add(SortAlong(segment));
                }
            }
        }

        while (nextLoose < loose.Count)
        {
            ordered.Add(SortAlong(loose[nextLoose++]));
        }

        return ordered;
    }

    /// <summary>The 3.28 order: lines top to bottom, each sorted along its baseline.</summary>
    internal static List<List<TextFragment>> RowOrder(List<List<TextFragment>> lines)
    {
        List<List<TextFragment>> ordered = new List<List<TextFragment>>(lines);
        ordered.Sort((a, b) => b[0].Y.CompareTo(a[0].Y));
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i] = SortAlong(ordered[i]);
        }

        return ordered;
    }

    private static List<TextFragment> SortAlong(List<TextFragment> line)
    {
        double angle = line[0].Angle;
        List<TextFragment> sorted = new List<TextFragment>(line);
        sorted.Sort((a, b) => TextGeometry.Along(a, angle).CompareTo(TextGeometry.Along(b, angle)));
        return sorted;
    }

    // The fragment's advance; when the font's widths are not known, half an
    // em per character (closer to real text than the 0.6 the word-gap
    // estimate uses, so a gutter is not hidden by overestimated lines).
    private static double Extent(TextFragment f) =>
        double.IsNaN(f.Width) ? f.Text.Length * f.FontSize * 0.5 : f.Width;

    private static bool IsHorizontal(TextFragment f) =>
        Math.Abs(Math.IEEERemainder(f.Angle, 360.0)) < HorizontalTolerance;

    // The band holding most of the line's characters, or -1 when none holds any.
    private static int LineBand(IReadOnlyList<LayoutBand> bands, List<TextFragment> line)
    {
        Dictionary<int, int> chars = new Dictionary<int, int>();
        int best = -1;
        int bestChars = 0;
        foreach (TextFragment f in line)
        {
            int b = BandOf(bands, f);
            if (b < 0)
            {
                continue;
            }

            int count = chars.TryGetValue(b, out int c) ? c : 0;
            count += Math.Max(1, f.Text.Trim().Length);
            chars[b] = count;
            if (count > bestChars || (count == bestChars && b < best))
            {
                best = b;
                bestChars = count;
            }
        }

        return best;
    }

    // The first band (in reading order) whose rectangle holds the fragment's
    // origin, or -1.
    private static int BandOf(IReadOnlyList<LayoutBand> bands, TextFragment f)
    {
        for (int b = 0; b < bands.Count; b++)
        {
            PdfRectangle r = bands[b].Bounds;
            if (f.X >= Math.Min(r.X1, r.X2) - 0.5 && f.X <= Math.Max(r.X1, r.X2) + 0.5
                && f.Y >= Math.Min(r.Y1, r.Y2) - 0.5 && f.Y <= Math.Max(r.Y1, r.Y2) + 0.5)
            {
                return b;
            }
        }

        return -1;
    }

    // The column holding the fragment's origin, or the nearest one.
    private static int ColumnOf(LayoutBand band, TextFragment f)
    {
        int best = 0;
        double bestDistance = double.PositiveInfinity;
        for (int c = 0; c < band.Columns.Count; c++)
        {
            PdfRectangle r = band.Columns[c].Bounds;
            double lo = Math.Min(r.X1, r.X2);
            double hi = Math.Max(r.X1, r.X2);
            double distance = f.X < lo ? lo - f.X : f.X > hi ? f.X - hi : 0.0;
            if (distance < bestDistance)
            {
                best = c;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Decides whether a run of lines sharing white space is read in columns.
    private static Band Close(List<LineBox> lines, List<(double Lo, double Hi)> common, double size)
    {
        if (lines.Count < 2)
        {
            return new Band(lines, null);
        }

        List<(double Lo, double Hi)> gutters = new List<(double Lo, double Hi)>();
        foreach ((double Lo, double Hi) g in common)
        {
            int leftLines = 0;
            int rightLines = 0;
            foreach (LineBox line in lines)
            {
                leftLines += line.X0 < g.Lo ? 1 : 0;
                rightLines += line.X1 > g.Hi ? 1 : 0;
            }

            if (leftLines >= 2 && rightLines >= 2)
            {
                gutters.Add(g);
            }
        }

        if (gutters.Count == 0 || gutters.Count >= MaxColumns)
        {
            return new Band(lines, null);
        }

        gutters.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        return new Band(lines, ProseColumns(lines, gutters, size));
    }

    // The columns when every one of them looks like running text, else null.
    private static List<(double X0, double X1)>? ProseColumns(List<LineBox> lines, List<(double Lo, double Hi)> gutters, double size)
    {
        int count = gutters.Count + 1;
        List<(double X0, double X1, int Chars)>[] segments = new List<(double, double, int)>[count];
        for (int c = 0; c < count; c++)
        {
            segments[c] = new List<(double, double, int)>();
        }

        foreach (LineBox line in lines)
        {
            double[] x0 = new double[count];
            double[] x1 = new double[count];
            int[] chars = new int[count];
            Array.Fill(x0, double.PositiveInfinity);
            Array.Fill(x1, double.NegativeInfinity);
            foreach (TextFragment f in line.Fragments)
            {
                double end = f.X + Extent(f);
                int c = 0;
                while (c < gutters.Count && (f.X + end) / 2.0 > (gutters[c].Lo + gutters[c].Hi) / 2.0)
                {
                    c++;
                }

                x0[c] = Math.Min(x0[c], f.X);
                x1[c] = Math.Max(x1[c], end);
                chars[c] += f.Text.Trim().Length;
            }

            for (int c = 0; c < count; c++)
            {
                if (chars[c] > 0)
                {
                    segments[c].Add((x0[c], x1[c], chars[c]));
                }
            }
        }

        List<(double X0, double X1)> columns = new List<(double X0, double X1)>(count);
        for (int c = 0; c < count; c++)
        {
            List<(double X0, double X1, int Chars)> segs = segments[c];
            if (segs.Count < 2)
            {
                return null;
            }

            double lo = double.PositiveInfinity;
            double hi = double.NegativeInfinity;
            foreach ((double X0, double X1, int Chars) s in segs)
            {
                lo = Math.Min(lo, s.X0);
                hi = Math.Max(hi, s.X1);
            }

            double width = hi - lo;
            if (width < MinColumnEm * size)
            {
                return null;
            }

            // Lines that start well inside the column (table cells, centred
            // captions) do not count for or against it.
            int aligned = 0;
            int full = 0;
            List<int> chars = new List<int>(segs.Count);
            foreach ((double X0, double X1, int Chars) s in segs)
            {
                if (s.X0 - lo > IndentShare * width)
                {
                    continue;
                }

                aligned++;
                full += s.X1 - s.X0 >= FillRatio * width ? 1 : 0;
                chars.Add(s.Chars);
            }

            if (aligned < 2)
            {
                return null;
            }

            chars.Sort();
            if (full < FillShare * aligned || chars[chars.Count / 2] < MinMedianChars)
            {
                return null;
            }

            columns.Add((lo, hi));
        }

        return columns;
    }

    // The line's text extents, merging gaps narrower than a gutter.
    private static List<(double Lo, double Hi)> Coverage(List<TextFragment> fragments, double minGutter)
    {
        List<(double Lo, double Hi)> spans = new List<(double Lo, double Hi)>(fragments.Count);
        foreach (TextFragment f in fragments)
        {
            if (f.Text.Trim().Length > 0)
            {
                spans.Add((f.X, f.X + Math.Max(0.0, Extent(f))));
            }
        }

        spans.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        List<(double Lo, double Hi)> merged = new List<(double Lo, double Hi)>();
        foreach ((double Lo, double Hi) s in spans)
        {
            if (merged.Count > 0 && s.Lo - merged[^1].Hi < minGutter)
            {
                merged[^1] = (merged[^1].Lo, Math.Max(merged[^1].Hi, s.Hi));
            }
            else
            {
                merged.Add(s);
            }
        }

        return merged;
    }

    // The white space of a line between the page's left and right text edges.
    private static List<(double Lo, double Hi)> Free(LineBox line, double left, double right)
    {
        List<(double Lo, double Hi)> free = new List<(double Lo, double Hi)>();
        double at = left;
        foreach ((double Lo, double Hi) c in line.Cover)
        {
            if (c.Lo > at)
            {
                free.Add((at, c.Lo));
            }

            at = Math.Max(at, c.Hi);
        }

        if (right > at)
        {
            free.Add((at, right));
        }

        return free;
    }

    // Only white space with text on both sides of it on the page can be a
    // gutter: a ragged right edge is not one. A line with text on one side
    // only still keeps a gutter going (its white space reaches the edge and
    // contains the gutter).
    private static List<(double Lo, double Hi)> Interior(List<(double Lo, double Hi)> spaces, double left, double right)
    {
        spaces.RemoveAll(g => g.Lo <= left + 0.5 || g.Hi >= right - 0.5);
        return spaces;
    }

    private static List<(double Lo, double Hi)> Intersect(
        List<(double Lo, double Hi)> a, List<(double Lo, double Hi)> b, double minWidth)
    {
        List<(double Lo, double Hi)> result = new List<(double Lo, double Hi)>();
        foreach ((double Lo, double Hi) x in a)
        {
            foreach ((double Lo, double Hi) y in b)
            {
                double lo = Math.Max(x.Lo, y.Lo);
                double hi = Math.Min(x.Hi, y.Hi);
                if (hi - lo >= minWidth)
                {
                    result.Add((lo, hi));
                }
            }
        }

        return result;
    }

    private static double MedianSize(List<LineBox> boxes)
    {
        List<double> sizes = new List<double>(boxes.Count);
        foreach (LineBox b in boxes)
        {
            sizes.Add(b.Size);
        }

        sizes.Sort();
        return sizes[sizes.Count / 2];
    }

    private sealed class LineBox
    {
        internal LineBox(List<TextFragment> fragments)
        {
            Fragments = fragments;
            Y = fragments[0].Y;
            double x0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity;
            double size = 0;
            foreach (TextFragment f in fragments)
            {
                x0 = Math.Min(x0, f.X);
                x1 = Math.Max(x1, f.X + Extent(f));
                size = Math.Max(size, f.FontSize);
            }

            X0 = x0;
            X1 = x1;
            Size = size;
        }

        internal List<TextFragment> Fragments { get; }

        internal double Y { get; }

        internal double X0 { get; }

        internal double X1 { get; }

        internal double Size { get; }

        internal List<(double Lo, double Hi)> Cover { get; set; } = new List<(double Lo, double Hi)>();
    }

    private sealed class Band
    {
        internal Band(List<LineBox> lines, List<(double X0, double X1)>? columns)
        {
            Lines = lines;
            Columns = columns;
        }

        internal List<LineBox> Lines { get; }

        // The columns' horizontal extents, or null when read as rows.
        internal List<(double X0, double X1)>? Columns { get; }

        internal double TopBaseline
        {
            get
            {
                double y = double.NegativeInfinity;
                foreach (LineBox line in Lines)
                {
                    y = Math.Max(y, line.Y);
                }

                return y;
            }
        }

        internal double Bottom
        {
            get
            {
                double y = double.PositiveInfinity;
                foreach (LineBox line in Lines)
                {
                    y = Math.Min(y, line.Y);
                }

                return y;
            }
        }

        // The top edge of the first band: above its highest line's text.
        internal double Top
        {
            get
            {
                double y = double.NegativeInfinity;
                foreach (LineBox line in Lines)
                {
                    y = Math.Max(y, line.Y + line.Size);
                }

                return y;
            }
        }

        // The bottom edge of the last band: below its lowest line's descenders
        // and subscripts.
        internal double BottomEdge
        {
            get
            {
                double y = double.PositiveInfinity;
                foreach (LineBox line in Lines)
                {
                    y = Math.Min(y, line.Y - (0.6 * line.Size));
                }

                return y;
            }
        }

        internal (double X0, double X1) Extent
        {
            get
            {
                double x0 = double.PositiveInfinity;
                double x1 = double.NegativeInfinity;
                foreach (LineBox line in Lines)
                {
                    x0 = Math.Min(x0, line.X0);
                    x1 = Math.Max(x1, line.X1);
                }

                return (x0, x1);
            }
        }
    }
}
