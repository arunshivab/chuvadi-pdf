// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics; §9 — Text; §7.8 — Content streams
// PHASE: v2.0.0 R1 D3c-3 — PageRasterizer refactored as a PageDisplayList painter

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Rendering.Raster;
using GraphicsPath = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Rasterizes a PDF page to a <see cref="PixelBuffer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PageRasterizer"/> is the top-level public API for page rendering.
/// Since v2.0.0, the pipeline is two-stage:
/// </para>
/// <list type="number">
///   <item>
///     <see cref="DisplayListBuilder"/> interprets the page's content stream
///     and produces an immutable <see cref="PageDisplayList"/>. CTM and text
///     matrices are baked into each op's geometry; the list is renderer-neutral.
///   </item>
///   <item>
///     <see cref="PageRasterizer"/> walks the display list and paints each op
///     into a <see cref="PixelBuffer"/>. The painter handles scale and Y-flip
///     only; it does not interpret PDF operators.
///   </item>
/// </list>
/// <para>
/// Clipping recorded by the display list is honoured: each op's
/// <see cref="RenderOp.Clips"/> are transformed to device space and applied
/// as an intersection region by the <see cref="ScanlineRasterizer"/>.
/// Axis-aligned rectangular clips (the common <c>re W n</c> case) take a fast
/// path; arbitrary clip paths are evaluated per scanline against their fill
/// rule. Image painting honours the same region per pixel.
/// </para>
/// <para>
/// PDF 32000-1:2008 §8 — Graphics model.
/// </para>
/// </remarks>
public sealed class PageRasterizer
{
    private readonly PdfObjectStore _objects;
    private readonly RenderOptions _options;
    private readonly ScanlineRasterizer _scanline;
    private readonly StrokeExpander _stroke;

    /// <summary>
    /// Initialises a <see cref="PageRasterizer"/> for a document's object store.
    /// </summary>
    /// <param name="objects">The document's object store.</param>
    /// <param name="options">Rendering options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="objects"/> is null.
    /// </exception>
    public PageRasterizer(PdfObjectStore objects, RenderOptions? options = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _options = options ?? RenderOptions.Default;
        _scanline = new ScanlineRasterizer { AntiAlias = _options.AntiAlias };
        _stroke = new StrokeExpander();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Rasterizes a PDF page to a <see cref="PixelBuffer"/>.
    /// </summary>
    /// <param name="page">The page to rasterize.</param>
    /// <returns>A pixel buffer in BGRA format containing the rendered page.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> is null.
    /// </exception>
    public PixelBuffer Rasterize(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        int s = _options.SuperSample > 1 ? _options.SuperSample : 1;

        if (s == 1)
        {
            return RasterizeInternal(page, _options);
        }

        // Supersample: render at s times the resolution, then box-filter down.
        RenderOptions hi = new RenderOptions
        {
            Dpi = _options.Dpi * s,
            Background = _options.Background,
            FlatnessTolerance = _options.FlatnessTolerance,
            SuperSample = 1,
        };

        PixelBuffer big = RasterizeInternal(page, hi);
        (int dstW, int dstH) = _options.PixelSize(page.Width, page.Height);
        return Downsample(big, dstW, dstH, s);
    }

    private PixelBuffer RasterizeInternal(PdfPage page, RenderOptions options)
    {
        double pageW = page.Width;
        double pageH = page.Height;

        (int pixW, int pixH) = options.PixelSize(pageW, pageH);
        PixelBuffer buffer = new PixelBuffer(pixW, pixH);
        buffer.Clear(options.Background);

        PageDisplayList list = DisplayListBuilder.Build(page, _objects);

        if (list.Ops.Count == 0)
        {
            return buffer;
        }

        PageRasterizer painter = new PageRasterizer(_objects, options);
        painter.PaintDisplayList(list, buffer, pageH, Transform.Identity);
        return buffer;
    }

    private static PixelBuffer Downsample(PixelBuffer src, int dstW, int dstH, int s)
    {
        PixelBuffer dst = new PixelBuffer(dstW, dstH);

        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int sumB = 0;
                int sumG = 0;
                int sumR = 0;
                int sumA = 0;
                int count = 0;

                for (int dy = 0; dy < s; dy++)
                {
                    int sy = (y * s) + dy;

                    if (sy >= src.Height)
                    {
                        continue;
                    }

                    for (int dx = 0; dx < s; dx++)
                    {
                        int sx = (x * s) + dx;

                        if (sx >= src.Width)
                        {
                            continue;
                        }

                        (byte b, byte g, byte r, byte a) = src.GetPixelBgra(sx, sy);
                        sumB += b;
                        sumG += g;
                        sumR += r;
                        sumA += a;
                        count++;
                    }
                }

                if (count == 0)
                {
                    count = 1;
                }

                dst.SetPixelBgra(
                    x, y,
                    (byte)(sumB / count),
                    (byte)(sumG / count),
                    (byte)(sumR / count),
                    (byte)(sumA / count));
            }
        }

        return dst;
    }

    /// <summary>
    /// Rasterizes a page and encodes the result as PNG bytes.
    /// </summary>
    public byte[] RasterizeToPng(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        PixelBuffer buffer = Rasterize(page);
        ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);

        using (MemoryStream ms = new MemoryStream())
        {
            PngEncoder.Encode(frame, ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Rasterizes a page and encodes the result as a single-page CMYK TIFF
    /// (Photometric=5, 4 samples per pixel, PackBits compression).
    /// </summary>
    /// <remarks>
    /// The pixel buffer is rendered in RGB and converted to CMYK using the
    /// standard subtractive formula. This is NOT a colour-managed transform;
    /// for press-accurate output, layer an ICC transform on the
    /// <see cref="CmykImage"/> returned by <see cref="RasterizeToCmyk"/>.
    /// </remarks>
    public byte[] RasterizeToCmykTiff(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        CmykImage cmyk = RasterizeToCmyk(page);
        return CmykTiffEncoder.Encode(cmyk);
    }

    /// <summary>
    /// Rasterizes a page and returns the result as a <see cref="CmykImage"/>.
    /// </summary>
    /// <remarks>
    /// Uses the standard subtractive RGB→CMYK conversion. For press-accurate
    /// output, apply an ICC transform externally.
    /// </remarks>
    public CmykImage RasterizeToCmyk(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        PixelBuffer buffer = Rasterize(page);
        return CmykImage.FromBgra(buffer);
    }

    // ── Display list painter ──────────────────────────────────────────────

    /// <summary>
    /// Paints a display list into the pixel buffer.
    /// </summary>
    /// <param name="list">The display list to paint.</param>
    /// <param name="buffer">The destination pixel buffer.</param>
    /// <param name="pageHeight">
    /// The outer page's MediaBox height in PDF points. Used for Y-flipping
    /// from PDF user space (Y up, bottom-left origin) to device space
    /// (Y down, top-left origin). Always the OUTER page height, even when
    /// recursing into nested form XObjects.
    /// </param>
    /// <param name="outerTransform">
    /// Composes outer-page-space coordinates from this list's coordinates.
    /// Identity for the top-level page; pre-multiplied by the form XObject's
    /// CtmComposition for each nested call.
    /// </param>
    private void PaintDisplayList(
        PageDisplayList list, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        foreach (RenderOp op in list.Ops)
        {
            switch (op)
            {
                case FillPathOp fp:
                    PaintFillOp(fp, buffer, pageHeight, outerTransform);
                    break;
                case StrokePathOp sp:
                    PaintStrokeOp(sp, buffer, pageHeight, outerTransform);
                    break;
                case DrawGlyphOp gp:
                    PaintGlyphOp(gp, buffer, pageHeight, outerTransform);
                    break;
                case DrawImageOp ip:
                    PaintImageOp(ip, buffer, pageHeight, outerTransform);
                    break;
                case NestedDisplayListOp np:
                    PaintNestedOp(np, buffer, pageHeight, outerTransform);
                    break;
            }
        }
    }

    // Snaps thin axis-aligned rectangles (hairline table borders) onto the
    // pixel grid so a sub-pixel-thin fill renders as one crisp, fully-covered
    // line instead of splitting its coverage faintly across two pixel rows or
    // columns. Only near-axis-aligned rectangles thinner than ~1.5px in one
    // dimension are affected; all other fills pass through unchanged.
    private static void SnapThinAxisAlignedRects(List<List<PointF>> subPaths)
    {
        const double thinThreshold = 1.5;

        foreach (List<PointF> sp in subPaths)
        {
            int n = sp.Count;

            // A rectangle is 4 distinct points, optionally repeating the first.
            bool closed = n == 5 && sp[0].X == sp[4].X && sp[0].Y == sp[4].Y;

            if (n != 4 && !closed)
            {
                continue;
            }

            double minX = sp[0].X, maxX = sp[0].X, minY = sp[0].Y, maxY = sp[0].Y;

            for (int i = 1; i < 4; i++)
            {
                if (sp[i].X < minX) { minX = sp[i].X; }
                if (sp[i].X > maxX) { maxX = sp[i].X; }
                if (sp[i].Y < minY) { minY = sp[i].Y; }
                if (sp[i].Y > maxY) { maxY = sp[i].Y; }
            }

            // Verify every vertex sits on a corner of the bounding box, i.e.
            // the shape is an axis-aligned rectangle (no diagonal edges).
            bool axisAligned = true;

            for (int i = 0; i < 4; i++)
            {
                bool onX = sp[i].X == minX || sp[i].X == maxX;
                bool onY = sp[i].Y == minY || sp[i].Y == maxY;

                if (!onX || !onY)
                {
                    axisAligned = false;
                    break;
                }
            }

            if (!axisAligned)
            {
                continue;
            }

            double width = maxX - minX;
            double height = maxY - minY;

            // Thin horizontal bar: snap the Y extent onto one pixel row.
            if (height < thinThreshold && width >= thinThreshold)
            {
                double center = (minY + maxY) / 2.0;
                double top = Math.Floor(center);
                double bottom = top + 1.0;
                ReplaceY(sp, minY, top);
                ReplaceY(sp, maxY, bottom);
            }
            // Thin vertical bar: snap the X extent onto one pixel column.
            else if (width < thinThreshold && height >= thinThreshold)
            {
                double center = (minX + maxX) / 2.0;
                double left = Math.Floor(center);
                double right = left + 1.0;
                ReplaceX(sp, minX, left);
                ReplaceX(sp, maxX, right);
            }
        }
    }

    private static void ReplaceX(List<PointF> sp, double oldX, double newX)
    {
        for (int i = 0; i < sp.Count; i++)
        {
            if (sp[i].X == oldX)
            {
                sp[i] = new PointF(newX, sp[i].Y);
            }
        }
    }

    private static void ReplaceY(List<PointF> sp, double oldY, double newY)
    {
        for (int i = 0; i < sp.Count; i++)
        {
            if (sp[i].Y == oldY)
            {
                sp[i] = new PointF(sp[i].X, newY);
            }
        }
    }

    // Snaps thin axis-aligned stroke segments onto pixel centres so a
    // hairline stroke renders as a single crisp, fully-covered pixel line
    // rather than splitting its coverage faintly across two rows/columns.
    private static void SnapThinStrokePath(List<List<PointF>> subPaths)
    {
        const double tolerance = 0.01;

        foreach (List<PointF> sp in subPaths)
        {
            for (int i = 0; i < sp.Count - 1; i++)
            {
                PointF a = sp[i];
                PointF b = sp[i + 1];

                double dx = Math.Abs(a.X - b.X);
                double dy = Math.Abs(a.Y - b.Y);

                if (dy <= tolerance && dx > tolerance)
                {
                    // Horizontal segment: snap its shared Y to a pixel centre.
                    double snapY = Math.Floor(a.Y) + 0.5;
                    sp[i] = new PointF(a.X, snapY);
                    sp[i + 1] = new PointF(b.X, snapY);
                }
                else if (dx <= tolerance && dy > tolerance)
                {
                    // Vertical segment: snap its shared X to a pixel centre.
                    double snapX = Math.Floor(a.X) + 0.5;
                    sp[i] = new PointF(snapX, a.Y);
                    sp[i + 1] = new PointF(snapX, b.Y);
                }
            }
        }
    }

    private void PaintFillOp(
        FillPathOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        GraphicsPath device = UserSpacePathToDevice(op.Path, pageHeight, outerTransform);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(device);
        SnapThinAxisAlignedRects(subPaths);
        ClipRegion? clip = BuildClipRegion(op.Clips, pageHeight, outerTransform);
        _scanline.Fill(buffer, subPaths, op.Color, op.Rule, clip);
    }

    private void PaintStrokeOp(
        StrokePathOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        GraphicsPath device = UserSpacePathToDevice(op.Path, pageHeight, outerTransform);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(device);

        // Stroke width in op.Style is in PDF user-space points; scale to device.
        double deviceWidth = op.Style.Width * _options.Scale;

        // Hairline crispening: a thin axis-aligned stroke (e.g. a table-cell
        // border drawn as "re S") whose 1px-wide body straddles a pixel
        // boundary splits its coverage across two rows/columns and renders
        // faint or broken. Snap each axis-aligned segment centre onto a pixel
        // centre and clamp the width to at least one device pixel so the line
        // lands fully within a single row/column at full coverage.
        if (deviceWidth <= 1.5)
        {
            SnapThinStrokePath(subPaths);
            deviceWidth = Math.Max(1.0, deviceWidth);
        }

        StrokeStyle deviceStyle = new StrokeStyle
        {
            Width = deviceWidth,
            Cap = op.Style.Cap,
            Join = op.Style.Join,
            MiterLimit = op.Style.MiterLimit,
            DashPattern = op.Style.DashPattern,
            DashOffset = op.Style.DashOffset,
            Color = op.Style.Color,
        };

        List<List<PointF>> filled = _stroke.Expand(subPaths, deviceStyle);
        ClipRegion? clip = BuildClipRegion(op.Clips, pageHeight, outerTransform);
        _scanline.Fill(buffer, filled, op.Style.Color, FillRule.NonZeroWinding, clip);
    }

    private void PaintGlyphOp(
        DrawGlyphOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        // The glyph outline is in PDF user space with textMatrix and CTM
        // already applied by DisplayListBuilder. Transform to device space.
        GraphicsPath device = UserSpacePathToDevice(op.Path, pageHeight, outerTransform);

        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(device);
        ClipRegion? clip = BuildClipRegion(op.Clips, pageHeight, outerTransform);
        _scanline.Fill(buffer, subPaths, op.Color, FillRule.NonZeroWinding, clip);
    }

    private void PaintImageOp(
        DrawImageOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        Transform imageToOuter = op.DeviceTransform.Multiply(outerTransform);
        double scale = _options.Scale;

        // Axis-aligned shortcut (matches pre-v2 behaviour exactly):
        //   destX = E * scale
        //   destY = (pageH - F) * scale - destH
        //   destW = A * scale
        //   destH = |D| * scale
        // For rotated/skewed images, this approximation collapses to the
        // axis-aligned bounding box. v2.1 will use a proper image transform.
        double destX = imageToOuter.E * scale;
        double destY = (pageHeight - imageToOuter.F) * scale;
        double destW = imageToOuter.A * scale;
        double destH = Math.Abs(imageToOuter.D) * scale;

        if (destW <= 0 || destH <= 0)
        {
            return;
        }

        ClipRegion? clip = BuildClipRegion(op.Clips, pageHeight, outerTransform);

        if (clip is not null && clip.IsEmpty)
        {
            return;
        }

        CompositeImage(op.Image, buffer, destX, destY - destH, destW, destH, clip);
    }

    private void PaintNestedOp(
        NestedDisplayListOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform)
    {
        // Compose the form XObject's contribution: inner-space → outer-space
        // is op.CtmComposition; outer-space → page-space is outerTransform.
        Transform innerToPage = op.CtmComposition.Multiply(outerTransform);
        PaintDisplayList(op.Inner, buffer, pageHeight, innerToPage);
    }

    // ── Clip helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a device-space <see cref="ClipRegion"/> from an op's clip paths,
    /// or returns null when the op is unclipped. Each clip path is transformed
    /// through the same user-space-to-device pipeline as the op's own geometry,
    /// so the clip and the painted content share one coordinate frame.
    /// </summary>
    private ClipRegion? BuildClipRegion(
        IReadOnlyList<ClipPath> clips, double pageHeight, Transform outerTransform)
    {
        if (clips.Count == 0)
        {
            return null;
        }

        List<List<List<PointF>>> deviceClips = new List<List<List<PointF>>>(clips.Count);
        List<FillRule> rules = new List<FillRule>(clips.Count);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);

        foreach (ClipPath clip in clips)
        {
            GraphicsPath device = UserSpacePathToDevice(clip.Path, pageHeight, outerTransform);
            deviceClips.Add(flattener.Flatten(device));
            rules.Add(clip.Rule);
        }

        return ClipRegion.Build(deviceClips, rules);
    }

    // ── Geometry helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Transforms a path from user space (Y up, bottom-left origin) to device
    /// pixel space (Y down, top-left origin), applying scale and Y-flip.
    /// </summary>
    /// <remarks>
    /// The display list's path coordinates are in PDF user space, with CTM
    /// already applied by <see cref="DisplayListBuilder"/>. This method only
    /// applies the device transform: scale by DPI and flip Y around the page
    /// height. When painting nested form XObjects, the form's CTM contribution
    /// is composed via <paramref name="outerTransform"/> before the device
    /// flip.
    /// </remarks>
    private GraphicsPath UserSpacePathToDevice(
        GraphicsPath source, double pageHeight, Transform outerTransform)
    {
        GraphicsPath result = new GraphicsPath();
        double scale = _options.Scale;

        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    PointF mp = ToDevice(seg.P0, outerTransform, scale, pageHeight);
                    result.MoveTo(mp.X, mp.Y);
                    break;
                case PathSegmentKind.LineTo:
                    PointF lp = ToDevice(seg.P0, outerTransform, scale, pageHeight);
                    result.LineTo(lp.X, lp.Y);
                    break;
                case PathSegmentKind.CubicBezierTo:
                    result.CubicBezierTo(
                        ToDevice(seg.P0, outerTransform, scale, pageHeight),
                        ToDevice(seg.P1, outerTransform, scale, pageHeight),
                        ToDevice(seg.P2, outerTransform, scale, pageHeight));
                    break;
                case PathSegmentKind.ClosePath:
                    result.ClosePath();
                    break;
            }
        }

        return result;
    }

    private static PointF ToDevice(PointF p, Transform outerTransform, double scale, double pageHeight)
    {
        // Apply the outer transform (identity for top-level page; the form
        // XObject composition for nested calls), then PDF→device:
        //   device_x = user_x * scale
        //   device_y = (pageH - user_y) * scale
        PointF outer = outerTransform.TransformPoint(p);
        return new PointF(outer.X * scale, (pageHeight - outer.Y) * scale);
    }

    private static void CompositeImage(
        ImageFrame frame, PixelBuffer buffer,
        double x, double y, double w, double h,
        ClipRegion? clip)
    {
        int dstX0 = Math.Max(0, (int)Math.Round(x));
        int dstY0 = Math.Max(0, (int)Math.Round(y));
        int dstX1 = Math.Min(buffer.Width - 1, (int)Math.Round(x + w));
        int dstY1 = Math.Min(buffer.Height - 1, (int)Math.Round(y + h));

        for (int py = dstY0; py <= dstY1; py++)
        {
            // Allowed x-intervals from the clip region for this row (null = all).
            List<(double Start, double End)>? allowed =
                clip?.AllowedIntervals(py + 0.5);

            if (allowed is not null && allowed.Count == 0)
            {
                continue;
            }

            for (int px = dstX0; px <= dstX1; px++)
            {
                if (allowed is not null && !InAnyInterval(allowed, px + 0.5))
                {
                    continue;
                }

                double srcFracX = (px - x) / w;
                double srcFracY = (py - y) / h;
                int srcX = (int)(srcFracX * frame.Width);
                int srcY = (int)(srcFracY * frame.Height);
                srcX = Math.Max(0, Math.Min(frame.Width - 1, srcX));
                srcY = Math.Max(0, Math.Min(frame.Height - 1, srcY));

                (byte sb, byte sg, byte sr, byte sa) = frame.Pixels.GetPixelBgra(srcX, srcY);
                buffer.SetPixelBgra(px, py, sb, sg, sr, sa);
            }
        }
    }

    private static bool InAnyInterval(List<(double Start, double End)> intervals, double x)
    {
        foreach ((double Start, double End) interval in intervals)
        {
            if (x >= interval.Start && x < interval.End)
            {
                return true;
            }
        }

        return false;
    }
}
