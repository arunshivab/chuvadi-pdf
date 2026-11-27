// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text objects), §8.10 (form XObjects),
//        §12.5 (annotations)
// PHASE: 3.35.0 (D-396, audit A-08) — find pattern matches across text
//        operators, inside form XObjects and Type 3 glyphs, and in annotations.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Redaction;

// Reads a page as a viewer draws it (every layer, forms flattened, Type 3
// glyphs, annotation appearances) and matches the patterns on the text joined
// across operators. Each match on page content becomes page-space boxes, one
// per line, inside the matched glyphs' ink, for the rectangle redaction to
// remove; a match in an annotation (its appearance or its text entries)
// names the annotation, which is removed (a widget's value is cleared).
internal static class PatternLocator
{
    private const int MaxDepth = 8;

    // Tile copies of a pattern cell listed when the display list describes a
    // repeating pattern by its region instead of its tiles.
    private const int MaxTileCopies = 4096;

    // Actions followed through /Next.
    private const int MaxActions = 64;

    internal sealed class PageMatches
    {
        // Inside the matched glyphs, for removal.
        internal List<RectangleF> Boxes { get; } = new List<RectangleF>();

        // The matched glyphs' full boxes, for the overlay.
        internal List<RectangleF> OverlayBoxes { get; } = new List<RectangleF>();

        internal HashSet<PdfDictionary> Annotations { get; } = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        internal List<string> Found { get; } = new List<string>();

        // Matches that could not be given a place on the page (a pattern cell
        // repeated more often than can be listed).
        internal List<string> Unplaced { get; } = new List<string>();

        internal bool Any => Boxes.Count > 0 || Annotations.Count > 0 || Unplaced.Count > 0;
    }

    private readonly record struct Glyph(
        string Text, bool Synthetic, double X0, double X1, double Y0, double Y1, RectangleF Full, double Baseline, double FontSize, PdfDictionary? Annotation);

    internal static PageMatches Locate(PdfDocument document, int pageIndex, IReadOnlyList<PatternRule> patterns)
    {
        PageMatches result = new PageMatches();
        if (patterns.Count == 0)
        {
            return result;
        }

        PageDisplayList list = DisplayListBuilder.Build(
            document, pageIndex, OptionalContentVisibility.AllVisible, AnnotationMode.View, flattenTransforms: true);
        List<Glyph> page = new List<Glyph>();
        Dictionary<PdfDictionary, List<Glyph>> annotations = new Dictionary<PdfDictionary, List<Glyph>>(ReferenceEqualityComparer.Instance);
        Walk walk = new Walk(patterns, result, annotations);
        Collect(list, AffineMatrix.Identity, page, walk, null, 0);

        // A string split across two content streams (not allowed, but drawn
        // by viewers) shows with or without a space at the split, depending
        // on the viewer; such a page is also matched with spaces ignored.
        // RED-18: text is matched in BOTH draw order and reading order, and the
        // union is redacted. Draw order catches a value whose pieces overlap at
        // a seam (where a position sort would misread them); reading order
        // catches a value drawn out of stream order (where draw order never
        // joins it up). A match found by both lands on the same glyphs, so the
        // redaction is idempotent.
        bool splitString = SplitsStringAcrossStreams(document, document.Pages[pageIndex].Dictionary);
        List<Glyph> pageReading = SortedReadingOrder(page);
        foreach ((int start, int end) in Matches(page, patterns, result.Found, splitString))
        {
            AddBoxes(page, start, end, result.Boxes, result.OverlayBoxes);
        }

        foreach ((int start, int end) in Matches(pageReading, patterns, result.Found, splitString))
        {
            AddBoxes(pageReading, start, end, result.Boxes, result.OverlayBoxes);
        }

        // Each tile copy of a pattern cell is matched on its own.
        foreach (List<Glyph> tile in walk.Tiles)
        {
            List<Glyph> tileReading = SortedReadingOrder(tile);
            foreach ((int start, int end) in Matches(tile, patterns, result.Found))
            {
                AddBoxes(tile, start, end, result.Boxes, result.OverlayBoxes);
            }

            foreach ((int start, int end) in Matches(tileReading, patterns, result.Found))
            {
                AddBoxes(tileReading, start, end, result.Boxes, result.OverlayBoxes);
            }
        }

        foreach (KeyValuePair<PdfDictionary, List<Glyph>> annotation in annotations)
        {
            if (Matches(annotation.Value, patterns, result.Found).Count > 0
                || Matches(SortedReadingOrder(annotation.Value), patterns, result.Found).Count > 0)
            {
                result.Annotations.Add(annotation.Key);
            }
        }

        // The annotations' own text entries, drawn or not.
        PdfDictionary pageDict = document.Pages[pageIndex].Dictionary;
        if (pageDict.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsValue)
            && document.Objects.Resolve(annotsValue) is PdfArray annots)
        {
            foreach (PdfPrimitive item in annots)
            {
                if (document.Objects.Resolve(item) is PdfDictionary annot && TextEntriesMatch(document, annot, patterns, result.Found))
                {
                    result.Annotations.Add(annot);
                }
            }
        }

        return result;
    }

    private static readonly string[] AnnotationTextKeys = { "Contents", "T", "TU", "TM", "Subj", "RC", "V", "DV" };

    private static bool TextEntriesMatch(PdfDocument document, PdfDictionary annot, IReadOnlyList<PatternRule> patterns, List<string> found)
    {
        bool hit = false;
        foreach (string key in AnnotationTextKeys)
        {
            if (annot.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null
                && TextOf(document, value) is string text && AnyMatch(text, patterns, found))
            {
                hit = true;
            }
        }

        // The appearance characteristics (/MK) captions.
        if (annot.TryGetValue(PdfName.Intern("MK"), out PdfPrimitive? mkValue) && document.Objects.Resolve(mkValue) is PdfDictionary mk)
        {
            foreach (string key in new[] { "CA", "RC", "AC" })
            {
                if (mk.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null
                    && TextOf(document, value) is string text && AnyMatch(text, patterns, found))
                {
                    hit = true;
                }
            }
        }

        // The annotation's actions (a link's URI, JavaScript) and its file.
        if (annot.TryGetValue(PdfName.Intern("A"), out PdfPrimitive? action) && ActionMatches(document, action, patterns, found))
        {
            hit = true;
        }

        if (annot.TryGetValue(PdfName.Intern("AA"), out PdfPrimitive? aaValue) && document.Objects.Resolve(aaValue) is PdfDictionary aa)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> trigger in aa)
            {
                if (ActionMatches(document, trigger.Value, patterns, found))
                {
                    hit = true;
                }
            }
        }

        if (annot.TryGetValue(PdfName.Intern("FS"), out PdfPrimitive? fs) && FileSpecNameMatches(document, fs, patterns, found))
        {
            hit = true;
        }

        // A widget's field value may sit on its parent field.
        if (annot.TryGetValue(PdfName.Intern("Parent"), out PdfPrimitive? parentValue)
            && document.Objects.Resolve(parentValue) is PdfDictionary parent)
        {
            foreach (string key in new[] { "V", "DV", "T", "TU" })
            {
                if (parent.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) && value is not null
                    && TextOf(document, value) is string text && AnyMatch(text, patterns, found))
                {
                    hit = true;
                }
            }
        }

        return hit;
    }

    // An action, and those chained to it through /Next: its URI, JavaScript,
    // destination name and file names.
    internal static bool ActionMatches(PdfDocument document, PdfPrimitive action, IReadOnlyList<PatternRule> patterns, List<string> found)
    {
        bool hit = false;
        Stack<PdfPrimitive> pending = new Stack<PdfPrimitive>();
        HashSet<PdfDictionary> seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push(action);
        while (pending.Count > 0 && seen.Count < MaxActions)
        {
            PdfPrimitive next = document.Objects.Resolve(pending.Pop());
            if (next is PdfArray chain)
            {
                foreach (PdfPrimitive item in chain)
                {
                    pending.Push(item);
                }

                continue;
            }

            if (next is not PdfDictionary dict || !seen.Add(dict))
            {
                continue;
            }

            foreach (string key in new[] { "URI", "JS", "D", "N" })
            {
                if (dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
                    && document.Objects.Resolve(value) is PdfString or PdfStream
                    && TextOf(document, value) is string text && AnyMatch(text, patterns, found))
                {
                    hit = true;
                }
            }

            if (dict.TryGetValue(PdfName.Intern("F"), out PdfPrimitive? file) && FileSpecNameMatches(document, file, patterns, found))
            {
                hit = true;
            }

            if (dict.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? following))
            {
                pending.Push(following);
            }
        }

        return hit;
    }

    // A file specification's names and description.
    internal static bool FileSpecNameMatches(PdfDocument document, PdfPrimitive fileSpec, IReadOnlyList<PatternRule> patterns, List<string> found)
    {
        PdfPrimitive resolved = document.Objects.Resolve(fileSpec);
        if (resolved is PdfString name)
        {
            return AnyMatch(name.ToTextString(), patterns, found);
        }

        bool hit = false;
        if (resolved is PdfDictionary spec)
        {
            foreach (string key in new[] { "F", "UF", "DOS", "Mac", "Unix", "Desc" })
            {
                if (spec.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value)
                    && document.Objects.Resolve(value) is PdfString text && AnyMatch(text.ToTextString(), patterns, found))
                {
                    hit = true;
                }
            }
        }

        return hit;
    }

    private static string? TextOf(PdfDocument document, PdfPrimitive value)
    {
        PdfPrimitive resolved = document.Objects.Resolve(value);
        switch (resolved)
        {
            case PdfString s:
                return s.ToTextString();
            case PdfName n:
                return n.Value;
            case PdfArray array:
                StringBuilder sb = new StringBuilder();
                foreach (PdfPrimitive item in array)
                {
                    if (document.Objects.Resolve(item) is PdfString part)
                    {
                        sb.Append(part.ToTextString()).Append('\n');
                    }
                }

                return sb.ToString();
            case PdfStream stream:
                try
                {
                    return Encoding.UTF8.GetString(Redactor.DecodeStream(stream, FilterRegistry.CreateDefaultPipeline()));
                }
                catch (Exception ex) when (ex is PdfException or InvalidOperationException or ArgumentException)
                {
                    return null;
                }

            default:
                return null;
        }
    }

    private static bool AnyMatch(string text, IReadOnlyList<PatternRule> patterns, List<string> found)
    {
        bool hit = false;
        foreach (PatternRule rule in patterns)
        {
            foreach (Match match in rule.Regex.Matches(text))
            {
                if (rule.Validator is null || rule.Validator(match.Value))
                {
                    found.Add(match.Value);
                    hit = true;
                }
            }
        }

        return hit;
    }

    private sealed class Walk
    {
        internal Walk(IReadOnlyList<PatternRule> patterns, PageMatches result, Dictionary<PdfDictionary, List<Glyph>> annotations)
        {
            Patterns = patterns;
            Result = result;
            Annotations = annotations;
        }

        internal IReadOnlyList<PatternRule> Patterns { get; }

        internal PageMatches Result { get; }

        internal Dictionary<PdfDictionary, List<Glyph>> Annotations { get; }

        internal List<List<Glyph>> Tiles { get; } = new List<List<Glyph>>();
    }

    private static void Collect(
        PageDisplayList list, AffineMatrix outer, List<Glyph> page, Walk walk,
        PdfDictionary? inAnnotation, int depth)
    {
        Dictionary<PdfDictionary, List<Glyph>> annotations = walk.Annotations;
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (RenderOp op in list)
        {
            PdfDictionary? owner = op.Annotation?.Dictionary ?? inAnnotation;
            switch (op)
            {
                case TextOp text:
                    List<Glyph> into = page;
                    if (owner is not null)
                    {
                        if (!annotations.TryGetValue(owner, out List<Glyph>? annotationGlyphs))
                        {
                            annotationGlyphs = new List<Glyph>();
                            annotations[owner] = annotationGlyphs;
                        }

                        into = annotationGlyphs;
                    }

                    AddGlyphs(text, outer, owner, into);
                    break;
                case Type3UseOp glyph:
                    Collect(glyph.Glyph, glyph.Composition.Multiply(outer), page, walk, owner, depth + 1);
                    break;
                case TilingPatternOp tiling:
                    CollectTiles(tiling, outer, walk, owner, depth);
                    break;
            }
        }
    }

    // Every tile copy of a pattern cell, each as its own run of glyphs. A
    // repeating pattern (described by its region) is listed tile by tile up
    // to MaxTileCopies; past that, a match in the cell cannot be placed.
    private static void CollectTiles(TilingPatternOp tiling, AffineMatrix outer, Walk walk, PdfDictionary? owner, int depth)
    {
        IReadOnlyList<AffineMatrix> tiles = tiling.Tiles;
        if (tiling.IsRepeating && !TryListTiles(tiling, out tiles))
        {
            List<Glyph> cellOnly = new List<Glyph>();
            Walk probe = new Walk(walk.Patterns, new PageMatches(), new Dictionary<PdfDictionary, List<Glyph>>(ReferenceEqualityComparer.Instance));
            Collect(tiling.Cell, AffineMatrix.Identity, cellOnly, probe, owner, depth + 1);
            List<string> found = new List<string>();
            if (Matches(cellOnly, walk.Patterns, found).Count > 0)
            {
                walk.Result.Unplaced.AddRange(found);
            }

            return;
        }

        foreach (AffineMatrix tile in tiles)
        {
            List<Glyph> glyphs = new List<Glyph>();
            Collect(tiling.Cell, tile.Multiply(outer), glyphs, walk, owner, depth + 1);
            if (glyphs.Count == 0)
            {
                continue;
            }

            if (owner is not null)
            {
                if (!walk.Annotations.TryGetValue(owner, out List<Glyph>? annotationGlyphs))
                {
                    annotationGlyphs = new List<Glyph>();
                    walk.Annotations[owner] = annotationGlyphs;
                }

                annotationGlyphs.AddRange(glyphs);
            }
            else
            {
                walk.Tiles.Add(glyphs);
            }
        }
    }

    private static bool TryListTiles(TilingPatternOp tiling, out IReadOnlyList<AffineMatrix> tiles)
    {
        tiles = Array.Empty<AffineMatrix>();
        AffineMatrix m = tiling.PatternMatrix;
        double det = (m.A * m.D) - (m.B * m.C);
        if (Math.Abs(det) < 1e-12 || tiling.XStep <= 0 || tiling.YStep <= 0)
        {
            return false;
        }

        // The region in pattern space.
        AffineMatrix inverse = new AffineMatrix(
            m.D / det, -m.B / det, -m.C / det, m.A / det,
            ((m.C * m.F) - (m.D * m.E)) / det, ((m.B * m.E) - (m.A * m.F)) / det);
        Rect r = tiling.Region;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach ((double x, double y) in new[] { (r.X, r.Y), (r.X + r.Width, r.Y), (r.X, r.Y + r.Height), (r.X + r.Width, r.Y + r.Height) })
        {
            (double px, double py) = inverse.Apply(x, y);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        double i0 = Math.Floor((minX - tiling.CellOriginX) / tiling.XStep) - 1;
        double i1 = Math.Ceiling((maxX - tiling.CellOriginX) / tiling.XStep);
        double j0 = Math.Floor((minY - tiling.CellOriginY) / tiling.YStep) - 1;
        double j1 = Math.Ceiling((maxY - tiling.CellOriginY) / tiling.YStep);
        double count = (i1 - i0 + 1) * (j1 - j0 + 1);
        if (!double.IsFinite(count) || count <= 0 || count > MaxTileCopies)
        {
            return false;
        }

        List<AffineMatrix> list = new List<AffineMatrix>((int)count);
        for (double j = j0; j <= j1; j++)
        {
            for (double i = i0; i <= i1; i++)
            {
                list.Add(new AffineMatrix(1, 0, 0, 1, i * tiling.XStep, j * tiling.YStep).Multiply(m));
            }
        }

        tiles = list;
        return true;
    }

    private static void AddGlyphs(TextOp text, AffineMatrix outer, PdfDictionary? owner, List<Glyph> into)
    {
        AffineMatrix m = text.Transform.Multiply(outer);
        double scale = Math.Sqrt(Math.Abs((m.A * m.D) - (m.B * m.C)));
        double size = Math.Abs(text.FontSize) * (scale > 0 ? scale : 1.0);
        foreach (DisplayListGlyph g in text.Glyphs)
        {
            if (string.IsNullOrEmpty(g.Unicode))
            {
                continue;
            }

            // The middle of the glyph's advance, from just below the baseline
            // to cap height: it meets this glyph's ink and not its neighbours
            // on the line, and stays clear of the lines above and below.
            double advance = Math.Abs(g.Advance) > 0 ? g.Advance : text.FontSize * 0.5;
            double x0 = g.X + (advance * 0.2);
            double x1 = g.X + (advance * 0.8);
            double y0 = g.Y - (text.FontSize * 0.15);
            double y1 = g.Y + (text.FontSize * 0.7);
            (double ax, double ay) = m.Apply(x0, y0);
            (double bx, double by) = m.Apply(x1, y0);
            (double cx, double cy) = m.Apply(x1, y1);
            (double dx, double dy) = m.Apply(x0, y1);
            (double ox, double oy) = m.Apply(g.X, g.Y);
            RectangleF full = Bounds(m, g.X, g.Y - (text.FontSize * 0.25), g.X + advance, g.Y + (text.FontSize * 0.85));
            double baseline = Math.Abs(m.B) > Math.Abs(m.A) ? ox : oy;
            into.Add(new Glyph(
                g.Unicode,
                g.CodeLength == 0,
                Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)),
                Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)),
                Math.Min(Math.Min(ay, by), Math.Min(cy, dy)),
                Math.Max(Math.Max(ay, by), Math.Max(cy, dy)),
                full,
                baseline,
                size,
                owner));
        }
    }

    // The matches, as glyph index ranges [start, end), over two joinings of
    // the glyphs: as drawn (only the text's own spaces, so a value split
    // across operators joins up), and with a space wherever the layout
    // leaves a gap. A line break ends a line in both.
    private static List<(int Start, int End)> Matches(
        List<Glyph> glyphs, IReadOnlyList<PatternRule> patterns, List<string> found, bool ignoringSpaces = false)
    {
        List<(int Start, int End)> ranges = new List<(int Start, int End)>();
        if (glyphs.Count == 0)
        {
            return ranges;
        }

        for (int variant = 0; variant < (ignoringSpaces ? 3 : 2); variant++)
        {
            StringBuilder text = new StringBuilder();
            List<int> owner = new List<int>();
            for (int i = 0; i < glyphs.Count; i++)
            {
                Glyph g = glyphs[i];
                if (i > 0)
                {
                    Glyph p = glyphs[i - 1];
                    double em = Math.Max(Math.Max(p.FontSize, g.FontSize), 0.01);
                    if (Math.Abs(g.Baseline - p.Baseline) > em * 0.5)
                    {
                        text.Append('\n');
                        owner.Add(-1);
                    }
                    else if (variant == 1 && Gap(p, g) > em * 0.25
                        && !string.IsNullOrWhiteSpace(g.Text) && !string.IsNullOrWhiteSpace(p.Text))
                    {
                        text.Append(' ');
                        owner.Add(-1);
                    }
                }

                // A control character (a line break inside a string split
                // across content streams) is no text a reader sees.
                if ((g.Synthetic || IsControl(g.Text)) && variant == 0)
                {
                    continue;
                }

                if (variant == 2 && string.IsNullOrWhiteSpace(g.Text))
                {
                    continue;
                }

                foreach (char ch in g.Text)
                {
                    text.Append(ch);
                    owner.Add(i);
                }
            }

            string joined = text.ToString();
            foreach (PatternRule rule in patterns)
            {
                foreach (Match match in rule.Regex.Matches(joined))
                {
                    if (rule.Validator is not null && !rule.Validator(match.Value))
                    {
                        continue;
                    }

                    int first = -1;
                    int last = -1;
                    for (int c = match.Index; c < match.Index + match.Length && c < owner.Count; c++)
                    {
                        if (owner[c] < 0)
                        {
                            continue;
                        }

                        if (first < 0)
                        {
                            first = owner[c];
                        }

                        last = owner[c];
                    }

                    if (first >= 0)
                    {
                        ranges.Add((first, last + 1));
                        found.Add(match.Value);
                    }
                }
            }
        }

        return ranges;
    }

    // True when a page's content is an array of streams and one of them
    // (other than the last) ends inside a string.
    private static bool SplitsStringAcrossStreams(PdfDocument document, PdfDictionary page)
    {
        if (!page.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? contents)
            || document.Objects.Resolve(contents) is not PdfArray streams
            || streams.Count < 2)
        {
            return false;
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        for (int i = 0; i < streams.Count - 1; i++)
        {
            if (document.Objects.Resolve(streams[i]) is not PdfStream stream)
            {
                continue;
            }

            byte[] data;
            try
            {
                data = Redactor.DecodeStream(stream, pipeline);
            }
            catch (Exception ex) when (ex is PdfException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (EndsInsideString(data))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsInsideString(byte[] data)
    {
        int depth = 0;
        bool hex = false;
        bool comment = false;
        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            if (comment)
            {
                comment = b != (byte)'\n' && b != (byte)'\r';
            }
            else if (depth > 0)
            {
                if (b == (byte)'\\')
                {
                    i++;
                }
                else if (b == (byte)'(')
                {
                    depth++;
                }
                else if (b == (byte)')')
                {
                    depth--;
                }
            }
            else if (hex)
            {
                hex = b != (byte)'>';
            }
            else if (b == (byte)'%')
            {
                comment = true;
            }
            else if (b == (byte)'(')
            {
                depth = 1;
            }
            else if (b == (byte)'<')
            {
                if (i + 1 < data.Length && data[i + 1] == (byte)'<')
                {
                    i++;
                }
                else
                {
                    hex = true;
                }
            }
        }

        return depth > 0 || hex;
    }

    private static bool IsControl(string text)
    {
        foreach (char ch in text)
        {
            if (!char.IsControl(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static RectangleF Union(RectangleF a, RectangleF b)
    {
        return RectangleF.FromCorners(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));
    }

    private static RectangleF Bounds(AffineMatrix m, double x0, double y0, double x1, double y1)
    {
        (double ax, double ay) = m.Apply(x0, y0);
        (double bx, double by) = m.Apply(x1, y0);
        (double cx, double cy) = m.Apply(x1, y1);
        (double dx, double dy) = m.Apply(x0, y1);
        return RectangleF.FromCorners(
            Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)),
            Math.Min(Math.Min(ay, by), Math.Min(cy, dy)),
            Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)),
            Math.Max(Math.Max(ay, by), Math.Max(cy, dy)));
    }

    // The space between two glyphs' advance boxes along the line.
    private static double Gap(Glyph previous, Glyph next)
    {
        double a = next.Full.X - previous.Full.Right;
        double b = previous.Full.X - next.Full.Right;
        return Math.Max(a, b);
    }

    // Returns the glyphs in reading order — top line first, left to right
    // within a line — without changing the caller's list (RED-18). Lines are
    // grouped by baseline within half an em, matching the line-break rule in
    // Matches; draw order breaks ties, so a line already in order keeps its
    // sequence and a match found in draw order is found again at the same place.
    private static List<Glyph> SortedReadingOrder(List<Glyph> glyphs)
    {
        if (glyphs.Count < 2)
        {
            return new List<Glyph>(glyphs);
        }

        int count = glyphs.Count;
        int[] byBaseline = new int[count];
        for (int i = 0; i < count; i++)
        {
            byBaseline[i] = i;
        }

        Array.Sort(byBaseline, (a, b) =>
        {
            int cmp = glyphs[b].Baseline.CompareTo(glyphs[a].Baseline);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        List<List<int>> lines = new List<List<int>>();
        foreach (int index in byBaseline)
        {
            if (lines.Count == 0)
            {
                lines.Add(new List<int> { index });
                continue;
            }

            List<int> current = lines[lines.Count - 1];
            Glyph head = glyphs[current[0]];
            Glyph glyph = glyphs[index];
            double em = Math.Max(Math.Max(head.FontSize, glyph.FontSize), 0.01);
            if (Math.Abs(glyph.Baseline - head.Baseline) <= em * 0.5)
            {
                current.Add(index);
            }
            else
            {
                lines.Add(new List<int> { index });
            }
        }

        List<Glyph> ordered = new List<Glyph>(count);
        foreach (List<int> line in lines)
        {
            line.Sort((a, b) =>
            {
                int cmp = glyphs[a].X0.CompareTo(glyphs[b].X0);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
            foreach (int index in line)
            {
                ordered.Add(glyphs[index]);
            }
        }

        return ordered;
    }

    // One box per line of the matched glyphs (ink-inside boxes joined).
    private static void AddBoxes(List<Glyph> glyphs, int start, int end, List<RectangleF> boxes, List<RectangleF> overlay)
    {
        int i = start;
        while (i < end)
        {
            Glyph first = glyphs[i];
            double x0 = first.X0;
            double x1 = first.X1;
            double y0 = first.Y0;
            double y1 = first.Y1;
            RectangleF cover = first.Full;
            int j = i + 1;
            while (j < end && Math.Abs(glyphs[j].Baseline - first.Baseline) <= Math.Max(first.FontSize, 0.01) * 0.5)
            {
                if (!glyphs[j].Synthetic && !string.IsNullOrWhiteSpace(glyphs[j].Text))
                {
                    x0 = Math.Min(x0, glyphs[j].X0);
                    x1 = Math.Max(x1, glyphs[j].X1);
                    y0 = Math.Min(y0, glyphs[j].Y0);
                    y1 = Math.Max(y1, glyphs[j].Y1);
                    cover = Union(cover, glyphs[j].Full);
                }

                j++;
            }

            boxes.Add(RectangleF.FromCorners(x0, y0, x1, y1));
            overlay.Add(cover);
            i = j;
        }
    }
}
