// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text), §8.9 (images), §8.5 (paths), §12.5 (annotations)
// PHASE: Redaction — verify-removal / extract-within-rectangle (PDF-03).

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Redaction;

/// <summary>The kind of content found inside a region.</summary>
public enum RedactionSurvivorKind
{
    /// <summary>A visible glyph (its approximate ink box meets the region).</summary>
    Text,

    /// <summary>
    /// A painted image whose placement meets the region and whose pixels there
    /// are not blanked (a partly redacted image with only blank pixels inside
    /// the region is not reported).
    /// </summary>
    Image,

    /// <summary>A painted path (fill or stroke ink) that meets the region.</summary>
    Path,

    /// <summary>An annotation whose rectangle meets the region.</summary>
    Annotation,
}

/// <summary>One piece of content found inside a region.</summary>
public sealed class RedactionSurvivor
{
    internal RedactionSurvivor(int pageIndex, RedactionSurvivorKind kind, RectangleF bounds, string? text)
    {
        PageIndex = pageIndex;
        Kind = kind;
        Bounds = bounds;
        Text = text;
    }

    /// <summary>Gets the zero-based page index.</summary>
    public int PageIndex { get; }

    /// <summary>Gets what kind of content this is.</summary>
    public RedactionSurvivorKind Kind { get; }

    /// <summary>Gets the content's bounding box in page space (points, origin bottom-left).</summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the text: the glyph's Unicode for <see cref="RedactionSurvivorKind.Text"/>,
    /// the annotation's <c>/Contents</c> for <see cref="RedactionSurvivorKind.Annotation"/>,
    /// otherwise null.
    /// </summary>
    public string? Text { get; }
}

/// <summary>The result of verifying that redaction regions are empty.</summary>
public sealed class RedactionVerification
{
    internal RedactionVerification(IReadOnlyList<RedactionSurvivor> survivors)
    {
        Survivors = survivors;
    }

    /// <summary>Gets everything still found inside the regions.</summary>
    public IReadOnlyList<RedactionSurvivor> Survivors { get; }

    /// <summary>Gets whether nothing survives inside any region.</summary>
    public bool IsClean => Survivors.Count == 0;

    /// <summary>Gets the surviving glyph text, concatenated in page and drawing order.</summary>
    public string SurvivingText
    {
        get
        {
            StringBuilder sb = new StringBuilder();
            foreach (RedactionSurvivor s in Survivors)
            {
                if (s.Kind == RedactionSurvivorKind.Text)
                {
                    sb.Append(s.Text);
                }
            }

            return sb.ToString();
        }
    }
}

/// <summary>
/// Checks what a document still paints inside given regions, using the same
/// display list the renderers draw — page content, form XObjects, Type 3
/// glyphs and annotation appearances included. Run it on a redacted document
/// with the redaction rectangles to confirm nothing visible remains (the
/// redaction overlay rectangle itself is ignored); or use
/// <see cref="ExtractWithin"/> on any document to list what a region holds.
/// </summary>
/// <remarks>
/// Text is matched per glyph on an approximate ink box (the middle 80% of the
/// glyph's advance, from the baseline to 0.7 em), so a neighbouring glyph
/// that merely abuts a rectangle is not reported. Invisible text (render mode
/// 3 or clip-only), fully transparent paint and shadings are
/// not reported.
/// </remarks>
public static class RedactionVerifier
{
    /// <summary>
    /// Verifies that the regions of <paramref name="rectangles"/> are empty in
    /// <paramref name="document"/> (typically the redaction output).
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="rectangles">The regions that must be empty.</param>
    /// <returns>The verification result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static RedactionVerification Verify(PdfDocument document, IReadOnlyList<RedactionRect> rectangles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rectangles);

        Dictionary<int, List<RectangleF>> byPage = new Dictionary<int, List<RectangleF>>();
        foreach (RedactionRect rect in rectangles)
        {
            ArgumentNullException.ThrowIfNull(rect);
            if (rect.PageIndex < 0 || rect.PageIndex >= document.PageCount)
            {
                continue;
            }

            if (!byPage.TryGetValue(rect.PageIndex, out List<RectangleF>? list))
            {
                list = new List<RectangleF>();
                byPage[rect.PageIndex] = list;
            }

            list.Add(rect.Bounds);
        }

        List<RedactionSurvivor> survivors = new List<RedactionSurvivor>();
        List<int> pages = new List<int>(byPage.Keys);
        pages.Sort();
        foreach (int page in pages)
        {
            Inspect(document, page, byPage[page], ignoreOverlays: true, survivors);
        }

        return new RedactionVerification(survivors);
    }

    /// <summary>
    /// Lists everything page <paramref name="pageIndex"/> paints inside
    /// <paramref name="region"/>: glyphs, images, paths and annotations.
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="region">The region, in page space (points, origin bottom-left).</param>
    /// <returns>The content found, in drawing order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the page index is out of range.</exception>
    public static IReadOnlyList<RedactionSurvivor> ExtractWithin(PdfDocument document, int pageIndex, RectangleF region)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "Page index is outside the document.");
        }

        List<RedactionSurvivor> found = new List<RedactionSurvivor>();
        Inspect(document, pageIndex, new List<RectangleF> { region }, ignoreOverlays: false, found);
        return found;
    }

    private static void Inspect(
        PdfDocument document, int pageIndex, List<RectangleF> regions, bool ignoreOverlays,
        List<RedactionSurvivor> into)
    {
        // Hidden layers are still in the file: verification sees every layer.
        PageDisplayList list = DisplayListBuilder.Build(document, pageIndex, OptionalContentVisibility.AllVisible);
        InspectList(list, AffineMatrix.Identity, pageIndex, regions, ignoreOverlays, into, 0);
        InspectAnnotations(document, pageIndex, regions, into);
    }

    private static void InspectList(
        PageDisplayList list, AffineMatrix outer, int pageIndex, List<RectangleF> regions,
        bool ignoreOverlays, List<RedactionSurvivor> into, int depth)
    {
        // The latest clip in each transform scope: a tiling pattern paints
        // only inside the clip that precedes it (its filled path).
        Stack<ClipOp?> clips = new Stack<ClipOp?>();
        ClipOp? clip = null;
        foreach (RenderOp op in list)
        {
            switch (op)
            {
                case TransformOp t when t.Push:
                    clips.Push(clip);
                    break;
                case TransformOp:
                    clip = clips.Count > 0 ? clips.Pop() : null;
                    break;
                case ClipOp c:
                    clip = c;
                    break;
                case TilingPatternOp tp when depth < 8 && (clip is null || ClipMeetsRegions(clip, outer, regions)):
                    InspectTiles(tp, outer, pageIndex, regions, ignoreOverlays, into, depth);
                    break;
                case TextOp t:
                    InspectText(t, outer, pageIndex, regions, into);
                    break;
                case ImageOp img when img.Alpha > 0:
                    AffineMatrix placement = img.Transform.Multiply(outer);
                    List<(double X, double Y)> quad = Quad(placement, 0, 0, 1, 1);
                    foreach (RectangleF r in regions)
                    {
                        if (PolygonMeetsRect(quad, r) && !IsImageBlankWithin(img, placement, r))
                        {
                            into.Add(new RedactionSurvivor(pageIndex, RedactionSurvivorKind.Image, Bounds(quad), null));
                            break;
                        }
                    }

                    break;
                case PathOp p when !p.Geometry.IsEmpty:
                    InspectPath(p, outer, pageIndex, regions, ignoreOverlays, into);
                    break;
                case Type3UseOp g when depth < 8:
                    InspectList(g.Glyph, g.Composition.Multiply(outer), pageIndex, regions, ignoreOverlays, into, depth + 1);
                    break;
            }
        }
    }

    // A partly redacted image keeps its pixels outside the region; it is
    // clean when every pixel whose cell reaches into the region is blank:
    // all colour samples zero (raw samples only), or fully transparent in
    // its soft mask or stencil over colour samples that are one value across
    // the whole image (a stencil's fill colour), so no picture hides under
    // zero alpha.
    private static bool IsImageBlankWithin(ImageOp img, AffineMatrix placement, RectangleF r)
    {
        double det = (placement.A * placement.D) - (placement.B * placement.C);
        if (Math.Abs(det) < 1e-12 || img.Format != ImageFormat.Raw || img.Width <= 0 || img.Height <= 0)
        {
            return false;
        }

        AffineMatrix inverse = new AffineMatrix(
            placement.D / det,
            -placement.B / det,
            -placement.C / det,
            placement.A / det,
            ((placement.C * placement.F) - (placement.D * placement.E)) / det,
            ((placement.B * placement.E) - (placement.A * placement.F)) / det);
        List<(double X, double Y)> unitRegion = Quad(inverse, r.X, r.Y, r.Right, r.Top);
        double minU = double.PositiveInfinity, minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity, maxV = double.NegativeInfinity;
        foreach ((double u, double v) in unitRegion)
        {
            minU = Math.Min(minU, u);
            minV = Math.Min(minV, v);
            maxU = Math.Max(maxU, u);
            maxV = Math.Max(maxV, v);
        }

        // A palette image expanded to colour: its blanked pixels are index
        // zero, whatever colour the palette gives that index.
        if (img.IndexSamples is byte[] indices && indices.LongLength >= (long)img.Width * img.Height
            && CellsBlank(placement, r, img.Width, img.Height, minU, minV, maxU, maxV,
                (col, row) => indices[(row * img.Width) + col] == 0))
        {
            return true;
        }

        int pixelBits = RawPixelBits(img);
        if (pixelBits <= 0)
        {
            return false;
        }

        long rowBytes = (((long)img.Width * pixelBits) + 7) / 8;
        byte[] data = img.PixelData;

        // Every colour sample in the region zero.
        bool coloursZero = CellsBlank(placement, r, img.Width, img.Height, minU, minV, maxU, maxV,
            (col, row) => PixelEquals(data, rowBytes, pixelBits, col, row));
        if (coloursZero)
        {
            return true;
        }

        if (img.SoftMaskAlpha is not byte[] alpha || img.SoftMaskWidth <= 0 || img.SoftMaskHeight <= 0
            || alpha.LongLength < (long)img.SoftMaskWidth * img.SoftMaskHeight)
        {
            return false;
        }

        // Transparent in the region, over colour samples that are one value
        // across the whole image (a stencil mask painted in the fill colour):
        // no picture hides under the zero alpha.
        for (int row = 0; row < img.Height; row++)
        {
            for (int col = 0; col < img.Width; col++)
            {
                if (!PixelEquals(data, rowBytes, pixelBits, col, row, 0, 0))
                {
                    return false;
                }
            }
        }

        return CellsBlank(placement, r, img.SoftMaskWidth, img.SoftMaskHeight, minU, minV, maxU, maxV,
            (col, row) => alpha[(row * img.SoftMaskWidth) + col] == 0);
    }

    // Bits per pixel of a raw image op, from its data length; 0 when no
    // component count, or more than one, fits the data exactly.
    private static int RawPixelBits(ImageOp img)
    {
        int bpc = img.BitsPerComponent;
        if (bpc <= 0)
        {
            return 0;
        }

        long Expected(int components) => ((((long)img.Width * components * bpc) + 7) / 8) * img.Height;

        // The colour-space enum cannot name every space (an ICC profile with
        // four channels reads as RGB), so the data length must single out
        // one component count; when several fit, the layout is unknown.
        int fit = 0;
        int fits = 0;
        for (int components = 1; components <= 32; components++)
        {
            if (Expected(components) == img.PixelData.LongLength)
            {
                fit = components;
                fits++;
            }
        }

        if (fits == 1)
        {
            return fit * bpc;
        }

        return 0;
    }

    // Compares one pixel's bits with zero (no reference pixel) or with the
    // pixel at (refCol, refRow).
    private static bool PixelEquals(
        byte[] data, long rowBytes, int pixelBits, int col, int row, int refCol = -1, int refRow = -1)
    {
        long first = (row * rowBytes * 8) + ((long)col * pixelBits);
        long other = refCol < 0 ? -1 : (refRow * rowBytes * 8) + ((long)refCol * pixelBits);
        for (int b = 0; b < pixelBits; b++)
        {
            long bit = first + b;
            int value = (data[bit >> 3] >> (7 - (int)(bit & 7))) & 1;
            int expected = 0;
            if (other >= 0)
            {
                long ob = other + b;
                expected = (data[ob >> 3] >> (7 - (int)(ob & 7))) & 1;
            }

            if (value != expected)
            {
                return false;
            }
        }

        return true;
    }

    // Visits the pixel cells (of a width x height grid stretched over the
    // unit square, row 0 at the top) whose page-space quad reaches into the
    // open region, returning false at the first cell that is not blank.
    private static bool CellsBlank(
        AffineMatrix placement, RectangleF r, int width, int height,
        double minU, double minV, double maxU, double maxV, Func<int, int, bool> isBlank)
    {
        int c0 = Math.Clamp((int)Math.Floor(minU * width) - 1, 0, width);
        int c1 = Math.Clamp((int)Math.Ceiling(maxU * width) + 1, 0, width);
        int r0 = Math.Clamp((int)Math.Floor((1.0 - maxV) * height) - 1, 0, height);
        int r1 = Math.Clamp((int)Math.Ceiling((1.0 - minV) * height) + 1, 0, height);
        for (int row = r0; row < r1; row++)
        {
            double v0 = 1.0 - ((double)(row + 1) / height);
            double v1 = 1.0 - ((double)row / height);
            for (int col = c0; col < c1; col++)
            {
                List<(double X, double Y)> cell = Quad(placement, (double)col / width, v0, (double)(col + 1) / width, v1);
                if (PolygonMeetsRect(cell, r) && !isBlank(col, row))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Most tile copies inspected per tiling-pattern op.
    private const int MaxInspectedTiles = 4096;

    // Inspects a tiling pattern's cell at every tile copy that can reach a
    // region (listed tiles, or the copies of a repeating pattern near each
    // region).
    private static void InspectTiles(
        TilingPatternOp tp, AffineMatrix outer, int pageIndex, List<RectangleF> regions,
        bool ignoreOverlays, List<RedactionSurvivor> into, int depth)
    {
        if (!tp.IsRepeating)
        {
            int count = 0;
            foreach (AffineMatrix tile in tp.Tiles)
            {
                if (++count > MaxInspectedTiles)
                {
                    break;
                }

                InspectList(tp.Cell, tile.Multiply(outer), pageIndex, regions, ignoreOverlays, into, depth + 1);
            }

            return;
        }

        AffineMatrix toPage = tp.PatternMatrix.Multiply(outer);
        double det = (toPage.A * toPage.D) - (toPage.B * toPage.C);
        if (Math.Abs(det) < 1e-12 || tp.XStep <= 0 || tp.YStep <= 0)
        {
            return;
        }

        AffineMatrix inverse = new AffineMatrix(
            toPage.D / det, -toPage.B / det, -toPage.C / det, toPage.A / det,
            ((toPage.C * toPage.F) - (toPage.D * toPage.E)) / det,
            ((toPage.B * toPage.E) - (toPage.A * toPage.F)) / det);
        foreach (RectangleF r in regions)
        {
            RectangleF b = Bounds(Quad(inverse, r.X, r.Y, r.Right, r.Top));

            // A cell may draw past one period: look a few copies further.
            double i0 = Math.Floor((b.X - tp.CellOriginX) / tp.XStep) - 2;
            double i1 = Math.Ceiling((b.Right - tp.CellOriginX) / tp.XStep) + 2;
            double j0 = Math.Floor((b.Y - tp.CellOriginY) / tp.YStep) - 2;
            double j1 = Math.Ceiling((b.Top - tp.CellOriginY) / tp.YStep) + 2;
            if ((i1 - i0 + 1) * (j1 - j0 + 1) > MaxInspectedTiles)
            {
                continue;
            }

            for (long i = (long)i0; i <= (long)i1; i++)
            {
                for (long j = (long)j0; j <= (long)j1; j++)
                {
                    AffineMatrix copy = new AffineMatrix(1, 0, 0, 1, i * tp.XStep, j * tp.YStep).Multiply(toPage);
                    InspectList(tp.Cell, copy, pageIndex, new List<RectangleF> { r }, ignoreOverlays, into, depth + 1);
                }
            }
        }
    }

    // Whether a clip region reaches into any region: its fill area for a
    // path clip, its outline's reach for a stroke-outline clip.
    private static bool ClipMeetsRegions(ClipOp clip, AffineMatrix outer, List<RectangleF> regions)
    {
        List<List<(double X, double Y)>> subpaths = Flatten(clip.Geometry, outer);
        double reach = clip.Stroke is null ? 0 : ClipOp.StrokeReach(clip.Stroke);
        foreach (RectangleF r in regions)
        {
            RectangleF grown = reach > 0
                ? new RectangleF(r.X - reach, r.Y - reach, r.Width + (2 * reach), r.Height + (2 * reach))
                : r;
            foreach (List<(double X, double Y)> subpath in subpaths)
            {
                if (subpath.Count >= 2 && PolygonMeetsRect(subpath, grown))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void InspectText(
        TextOp t, AffineMatrix outer, int pageIndex, List<RectangleF> regions, List<RedactionSurvivor> into)
    {
        if (t.RenderingMode is TextRenderingMode.Invisible or TextRenderingMode.Clip)
        {
            return;
        }

        AffineMatrix m = t.Transform.Multiply(outer);
        foreach (DisplayListGlyph g in t.Glyphs)
        {
            if (string.IsNullOrWhiteSpace(g.Unicode) || g.Advance <= 0)
            {
                continue;
            }

            double x0 = g.X + (g.Advance * 0.1);
            double x1 = g.X + (g.Advance * 0.9);
            double y0 = g.Y;
            double y1 = g.Y + (t.FontSize * 0.7);
            List<(double X, double Y)> quad = Quad(m, x0, y0, x1, y1);
            foreach (RectangleF r in regions)
            {
                if (PolygonMeetsRect(quad, r))
                {
                    into.Add(new RedactionSurvivor(pageIndex, RedactionSurvivorKind.Text, Bounds(quad), g.Unicode));
                    break;
                }
            }
        }
    }

    private static void InspectPath(
        PathOp p, AffineMatrix outer, int pageIndex, List<RectangleF> regions,
        bool ignoreOverlays, List<RedactionSurvivor> into)
    {
        bool fills = p.Mode != PaintMode.Stroke && p.FillOpacity > 0;
        bool strokes = p.Mode != PaintMode.Fill && p.StrokeOpacity > 0;
        if (!fills && !strokes)
        {
            return;
        }

        List<List<(double X, double Y)>> subpaths = Flatten(p.Geometry, outer);
        if (subpaths.Count == 0)
        {
            return;
        }

        // The redaction overlay itself: a filled rectangle matching a region.
        if (ignoreOverlays && fills && !strokes && IsOverlay(subpaths, regions))
        {
            return;
        }

        double halfWidth = strokes ? Math.Max(p.Stroke?.LineWidth ?? 1.0, 0.0) / 2.0 : 0.0;
        foreach (RectangleF r in regions)
        {
            bool hit = false;
            foreach (List<(double X, double Y)> sub in subpaths)
            {
                if (fills && sub.Count >= 3 && PolygonMeetsRect(sub, r))
                {
                    hit = true;
                }

                if (!hit && strokes)
                {
                    RectangleF grown = new RectangleF(
                        r.X - halfWidth, r.Y - halfWidth, r.Width + (2 * halfWidth), r.Height + (2 * halfWidth));
                    for (int i = 0; i + 1 < sub.Count && !hit; i++)
                    {
                        hit = SegmentMeetsRect(sub[i], sub[i + 1], grown);
                    }
                }

                if (hit)
                {
                    break;
                }
            }

            if (hit)
            {
                List<(double X, double Y)> all = new List<(double X, double Y)>();
                foreach (List<(double X, double Y)> sub in subpaths)
                {
                    all.AddRange(sub);
                }

                into.Add(new RedactionSurvivor(pageIndex, RedactionSurvivorKind.Path, Bounds(all), null));
                return;
            }
        }
    }

    private static void InspectAnnotations(
        PdfDocument document, int pageIndex, List<RectangleF> regions, List<RedactionSurvivor> into)
    {
        PdfDictionary page = document.Pages[pageIndex].Dictionary;
        if (!page.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? annotsValue)
            || document.Objects.Resolve(annotsValue) is not PdfArray annots)
        {
            return;
        }

        for (int i = 0; i < annots.Count; i++)
        {
            if (document.Objects.Resolve(annots[i]) is not PdfDictionary annot
                || !annot.TryGetValue(PdfName.Intern("Rect"), out PdfPrimitive? rectValue)
                || document.Objects.Resolve(rectValue) is not PdfArray rect
                || rect.Count < 4)
            {
                continue;
            }

            RectangleF bounds = RectangleF.FromCorners(
                Num(document, rect[0]), Num(document, rect[1]), Num(document, rect[2]), Num(document, rect[3]));
            foreach (RectangleF r in regions)
            {
                if (!bounds.Intersect(r).IsEmpty)
                {
                    string? contents = annot.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? cv)
                        && document.Objects.Resolve(cv) is PdfString s
                        ? s.ToTextString()
                        : null;
                    into.Add(new RedactionSurvivor(pageIndex, RedactionSurvivorKind.Annotation, bounds, contents));
                    break;
                }
            }
        }
    }

    // ── geometry ───────────────────────────────────────────────────────────

    private static double Num(PdfDocument document, PdfPrimitive p) => document.Objects.Resolve(p) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    private static List<(double X, double Y)> Quad(AffineMatrix m, double x0, double y0, double x1, double y1)
    {
        return new List<(double X, double Y)>
        {
            m.Apply(x0, y0),
            m.Apply(x1, y0),
            m.Apply(x1, y1),
            m.Apply(x0, y1),
        };
    }

    private static RectangleF Bounds(List<(double X, double Y)> points)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach ((double x, double y) in points)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return RectangleF.FromCorners(minX, minY, maxX, maxY);
    }

    // Flattens page-space path geometry (curves by their control polygon,
    // which bounds the curve) into closed-or-open polylines.
    private static List<List<(double X, double Y)>> Flatten(PathGeometry geometry, AffineMatrix outer)
    {
        List<List<(double X, double Y)>> subpaths = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        (double X, double Y) start = (0, 0);
        foreach (Chuvadi.Pdf.Rendering.DisplayList.PathSegment seg in geometry.Segments)
        {
            switch (seg.Command)
            {
                case PathCommand.MoveTo:
                    current = new List<(double X, double Y)> { outer.Apply(seg.X1, seg.Y1) };
                    start = current[0];
                    subpaths.Add(current);
                    break;
                case PathCommand.LineTo:
                    current?.Add(outer.Apply(seg.X1, seg.Y1));
                    break;
                case PathCommand.CubicTo:
                    current?.Add(outer.Apply(seg.X1, seg.Y1));
                    current?.Add(outer.Apply(seg.X2, seg.Y2));
                    current?.Add(outer.Apply(seg.X3, seg.Y3));
                    break;
                default:
                    if (current is not null && current.Count > 0)
                    {
                        current.Add(start);
                    }

                    break;
            }
        }

        subpaths.RemoveAll(s => s.Count == 0);
        return subpaths;
    }

    private static bool IsOverlay(List<List<(double X, double Y)>> subpaths, List<RectangleF> regions)
    {
        const double Tol = 0.05;
        if (subpaths.Count != 1)
        {
            return false;
        }

        RectangleF b = Bounds(subpaths[0]);
        foreach (RectangleF r in regions)
        {
            if (Math.Abs(b.X - r.X) < Tol && Math.Abs(b.Y - r.Y) < Tol
                && Math.Abs(b.Right - r.Right) < Tol && Math.Abs(b.Top - r.Top) < Tol)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PolygonMeetsRect(List<(double X, double Y)> poly, RectangleF r)
    {
        // Any vertex inside, any edge crossing, or the rectangle inside the polygon.
        foreach ((double x, double y) in poly)
        {
            if (x > r.X && x < r.Right && y > r.Y && y < r.Top)
            {
                return true;
            }
        }

        for (int i = 0; i < poly.Count; i++)
        {
            if (SegmentMeetsRect(poly[i], poly[(i + 1) % poly.Count], r))
            {
                return true;
            }
        }

        return PointInPolygon(poly, r.X + (r.Width / 2), r.Y + (r.Height / 2));
    }

    // Liang–Barsky: does the segment pass through the open rectangle interior?
    private static bool SegmentMeetsRect((double X, double Y) a, (double X, double Y) b, RectangleF r)
    {
        double t0 = 0, t1 = 1;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double[] p = { -dx, dx, -dy, dy };
        double[] q = { a.X - r.X, r.Right - a.X, a.Y - r.Y, r.Top - a.Y };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12)
            {
                if (q[i] <= 0)
                {
                    return false;
                }

                continue;
            }

            double t = q[i] / p[i];
            if (p[i] < 0)
            {
                t0 = Math.Max(t0, t);
            }
            else
            {
                t1 = Math.Min(t1, t);
            }

            if (t0 >= t1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointInPolygon(List<(double X, double Y)> poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            (double xi, double yi) = poly[i];
            (double xj, double yj) = poly[j];
            if (((yi > y) != (yj > y)) && (x < ((xj - xi) * (y - yi) / (yj - yi)) + xi))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
