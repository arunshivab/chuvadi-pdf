// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics; §9 — Text; §7.8 — Content streams
// PHASE: v2.0.0 R1 D3c-3 — PageRasterizer refactored as a PageDisplayList painter

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using GraphicsPath = Chuvadi.Pdf.Graphics.Path;
using PdfBlendMode = Chuvadi.Pdf.Rendering.DisplayList.PdfBlendMode;
using BlendModes = Chuvadi.Pdf.Rendering.DisplayList.BlendModes;
using Rect = Chuvadi.Pdf.Rendering.DisplayList.Rect;
using PathGeometry = Chuvadi.Pdf.Rendering.DisplayList.PathGeometry;
using PathGeometryAccessors = Chuvadi.Pdf.Rendering.DisplayList.PathGeometryAccessors;

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
    private readonly Dictionary<RasterSoftMaskInfo, float[]> _softMaskCache =
        new(ReferenceEqualityComparer.Instance);

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
        _scanline = new ScanlineRasterizer { AntiAlias = _options.AntiAlias, GammaCorrect = _options.GammaCorrect };
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
    public PixelBuffer Rasterize(PdfPage page) => Rasterize(page, CancellationToken.None);

    /// <summary>
    /// Rasterizes a PDF page to a <see cref="PixelBuffer"/>, stopping when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// The work runs in a <see cref="PdfWorkScope"/> with the limits the
    /// document was opened with (<see cref="PdfObjectStore.Limits"/>): content
    /// operators, decoded streams and buffers are checked as they are
    /// processed, and cancellation is noticed well within a second.
    /// </remarks>
    /// <param name="page">The page to rasterize.</param>
    /// <param name="cancellationToken">A token that cancels the render.</param>
    /// <returns>A pixel buffer in BGRA format containing the rendered page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="PdfLimitExceededException">A limit was exceeded.</exception>
    /// <exception cref="PdfException">
    /// The page could not be read or rendered. Failures the code did not
    /// anticipate are reported as <see cref="PdfCorruptionException"/> with
    /// the original as the inner exception.
    /// </exception>
    public PixelBuffer Rasterize(PdfPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Guard(() => RasterizeCore(page), cancellationToken);
    }

    // Runs work in a scope with the document's limits and the caller's token,
    // reporting failures outside the documented set as PdfCorruptionException.
    private T Guard<T>(Func<T> work, CancellationToken cancellationToken)
    {
        using PdfWorkScope scope = PdfWorkScope.Enter(_objects.Limits, cancellationToken);
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is not (PdfException or OperationCanceledException or OutOfMemoryException or ObjectDisposedException)
            && (ex is not IOException || ex is EndOfStreamException))
        {
            throw new PdfCorruptionException("The page could not be rendered: " + ex.Message, ex);
        }
    }

    private PixelBuffer RasterizeCore(PdfPage page)
    {
        if (!(page.Width > 0) || !(page.Height > 0))
        {
            throw new PdfCorruptionException("The page has an empty or invalid MediaBox.");
        }

        (int finalW, int finalH) = _options.PixelSize(page.Width, page.Height);
        PdfWorkScope.CheckOutputPixels(finalW, finalH);
        int rotation = _options.Rotation;
        if (_options.ApplyPageRotation)
        {
            // A malformed /Rotate that is not a multiple of 90 contributes its
            // nearest lower multiple rather than failing the render.
            int pageRotate = page.Rotate - (page.Rotate % 90);
            rotation = RenderOptions.NormalizeRotation(rotation + pageRotate, nameof(RenderOptions.Rotation));
        }

        return RotateClockwise(RasterizeUpright(page), rotation);
    }

    // Renders the page in its unrotated user space (the historical output).
    private PixelBuffer RasterizeUpright(PdfPage page)
    {
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
            AntiAlias = _options.AntiAlias,
            GammaCorrect = _options.GammaCorrect,
            Hinting = _options.Hinting,
            AutohintUnhintedFonts = _options.AutohintUnhintedFonts,
            IgnoreOptionalContent = _options.IgnoreOptionalContent,
            LayerVisibility = _options.LayerVisibility,
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
        PdfWorkScope.ChargeMemory((long)pixW * pixH * 4);
        PixelBuffer buffer = new PixelBuffer(pixW, pixH);
        buffer.Clear(options.Background);

        double hintScale = options.Hinting == HintingMode.Off ? 0.0 : options.Scale;
        bool lightHint = options.Hinting == HintingMode.Light;
        PageDisplayList list = DisplayListBuilder.Build(
            page, _objects, options.VisibilityFor(page), hintScale, lightHint, options.AutohintUnhintedFonts);

        if (list.Ops.Count == 0)
        {
            return buffer;
        }

        PageRasterizer painter = new PageRasterizer(_objects, options);
        painter.PaintDisplayList(list, buffer, pageH, Transform.Identity);
        return buffer;
    }

    // Exact pixel rotation (no resampling) for the clockwise view rotation.
    private static PixelBuffer RotateClockwise(PixelBuffer src, int rotation)
    {
        if (rotation == 0)
        {
            return src;
        }

        int w = src.Width;
        int h = src.Height;
        bool swap = rotation == 90 || rotation == 270;
        PdfWorkScope.ChargeMemory((long)w * h * 4);
        PixelBuffer dst = new PixelBuffer(swap ? h : w, swap ? w : h);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte b, byte g, byte r, byte a) = src.GetPixelBgra(x, y);
                (int dx, int dy) = rotation switch
                {
                    90 => (h - 1 - y, x),
                    180 => (w - 1 - x, h - 1 - y),
                    _ => (y, w - 1 - x),
                };
                dst.SetPixelBgra(dx, dy, b, g, r, a);
            }
        }

        return dst;
    }

    private static PixelBuffer Downsample(PixelBuffer src, int dstW, int dstH, int s)
    {
        PdfWorkScope.ChargeMemory((long)dstW * dstH * 4);
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
    public byte[] RasterizeToPng(PdfPage page) => RasterizeToPng(page, CancellationToken.None);

    /// <summary>
    /// Rasterizes a page and encodes the result as PNG bytes, stopping when
    /// <paramref name="cancellationToken"/> is cancelled. See
    /// <see cref="Rasterize(PdfPage, CancellationToken)"/> for limits and exceptions.
    /// </summary>
    /// <param name="page">The page to rasterize.</param>
    /// <param name="cancellationToken">A token that cancels the render.</param>
    /// <returns>The PNG bytes.</returns>
    public byte[] RasterizeToPng(PdfPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Guard(() => EncodePng(RasterizeCore(page)), cancellationToken);
    }

    private static byte[] EncodePng(PixelBuffer buffer)
    {
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

    // ── Region / clipped rendering ────────────────────────────────────────

    /// <summary>
    /// Renders a rectangular sub-region of a page to a <see cref="PixelBuffer"/>
    /// at the given resolution. The region is given in page (PDF user) space;
    /// the result is sized <c>region.Width * dpi/72</c> by
    /// <c>region.Height * dpi/72</c> pixels, with the region's top-left corner
    /// at the buffer origin. Lighter than rasterizing the whole page and
    /// cropping.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="region">The sub-region in page space (PDF points).</param>
    /// <param name="dpi">Output resolution in dots per inch. Must be positive.</param>
    /// <returns>The rendered region. Never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dpi"/> is not positive, or <paramref name="region"/> has
    /// a non-positive dimension.
    /// </exception>
    public PixelBuffer RenderRegion(PdfPage page, Rect region, double dpi) =>
        RenderRegion(page, region, dpi, CancellationToken.None);

    /// <summary>
    /// Renders a rectangular sub-region of a page (see
    /// <see cref="RenderRegion(PdfPage, Rect, double)"/>), stopping when
    /// <paramref name="cancellationToken"/> is cancelled. See
    /// <see cref="Rasterize(PdfPage, CancellationToken)"/> for limits and exceptions.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="region">The sub-region in page space (PDF points).</param>
    /// <param name="dpi">Output resolution in dots per inch. Must be positive.</param>
    /// <param name="cancellationToken">A token that cancels the render.</param>
    /// <returns>The rendered region. Never null.</returns>
    public PixelBuffer RenderRegion(PdfPage page, Rect region, double dpi, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive.");
        }
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region), "Region must have positive width and height.");
        }

        return Guard(
            () =>
            {
                RenderOptions options = WithDpi(dpi);
                double scale = dpi / 72.0;
                int width = Math.Max(1, (int)Math.Round(region.Width * scale));
                int height = Math.Max(1, (int)Math.Round(region.Height * scale));

                PdfWorkScope.ChargeRaster(width, height);
                PixelBuffer buffer = new PixelBuffer(width, height);
                buffer.Clear(options.Background);
                PaintRegion(page, options, region, buffer);
                return buffer;
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders the content inside a clip path to a <see cref="PixelBuffer"/> at
    /// the given resolution. The buffer is sized to the clip path's page-space
    /// bounding box (at <paramref name="dpi"/>); pixels outside the clip path
    /// are left fully transparent. The clip path is given in page (PDF user)
    /// space.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="clipPageSpace">The clip path in page space.</param>
    /// <param name="dpi">Output resolution in dots per inch. Must be positive.</param>
    /// <returns>
    /// The rendered, clipped region, sized to the clip's bounding box. Pixels
    /// outside the clip are transparent. Never null.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="page"/> or <paramref name="clipPageSpace"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dpi"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="clipPageSpace"/> has an empty bounding box.</exception>
    public PixelBuffer RenderClipped(PdfPage page, PathGeometry clipPageSpace, double dpi)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(clipPageSpace);
        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive.");
        }

        Rect bounds = PathGeometryAccessors.Bounds(clipPageSpace);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentException(
                "Clip path has an empty bounding box.", nameof(clipPageSpace));
        }

        RenderOptions options = WithDpi(dpi);
        double scale = dpi / 72.0;
        int width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        PdfWorkScope.ChargeRaster(width, height);
        PixelBuffer buffer = new PixelBuffer(width, height);
        buffer.Clear(options.Background);
        PaintRegion(page, options, bounds, buffer);
        MaskToClip(buffer, clipPageSpace, bounds, scale, options.FlatnessTolerance);
        return buffer;
    }

    /// <summary>
    /// Renders a sub-region (see <see cref="RenderRegion(PdfPage, Rect, double)"/>) and encodes it as
    /// 24-bit RGB PNG bytes.
    /// </summary>
    public byte[] RenderRegionToPng(PdfPage page, Rect region, double dpi)
    {
        PixelBuffer buffer = RenderRegion(page, region, dpi);
        ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);
        using (MemoryStream ms = new MemoryStream())
        {
            PngEncoder.Encode(frame, ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Renders a clipped region (see <see cref="RenderClipped"/>) and encodes it
    /// as 32-bit RGBA PNG bytes, preserving the transparency outside the clip.
    /// </summary>
    public byte[] RenderClippedToPng(PdfPage page, PathGeometry clipPageSpace, double dpi)
    {
        PixelBuffer buffer = RenderClipped(page, clipPageSpace, dpi);
        ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);
        using (MemoryStream ms = new MemoryStream())
        {
            PngEncoder.Encode(frame, ms, includeAlpha: true);
            return ms.ToArray();
        }
    }

    // Clones the instance options with a new DPI.
    private RenderOptions WithDpi(double dpi)
    {
        return new RenderOptions
        {
            Dpi = dpi,
            Background = _options.Background,
            FlatnessTolerance = _options.FlatnessTolerance,
            SuperSample = _options.SuperSample,
            AntiAlias = _options.AntiAlias,
            GammaCorrect = _options.GammaCorrect,
            Hinting = _options.Hinting,
            AutohintUnhintedFonts = _options.AutohintUnhintedFonts,
            IgnoreOptionalContent = _options.IgnoreOptionalContent,
            LayerVisibility = _options.LayerVisibility,
        };
    }

    // Paints the page's display list into a region-sized buffer. The outer
    // transform shifts the region's origin to (0,0); painting uses the region
    // height for the Y-flip so the region's top maps to the buffer's top row.
    private void PaintRegion(PdfPage page, RenderOptions options, Rect region, PixelBuffer buffer)
    {
        double hintScale = options.Hinting == HintingMode.Off ? 0.0 : options.Scale;
        bool lightHint = options.Hinting == HintingMode.Light;
        PageDisplayList list = DisplayListBuilder.Build(
            page, _objects, options.VisibilityFor(page), hintScale, lightHint, options.AutohintUnhintedFonts);
        if (list.Ops.Count == 0)
        {
            return;
        }

        PageRasterizer painter = new PageRasterizer(_objects, options);
        Transform outer = Transform.CreateTranslation(-region.X, -region.Y);
        painter.PaintDisplayList(list, buffer, region.Height, outer);
    }

    // Sets pixels whose centres fall outside the clip path to fully transparent.
    // The clip is flattened once; each pixel centre is mapped back to page space
    // and tested with the same non-zero winding rule the renderer's fills use.
    private static void MaskToClip(
        PixelBuffer buffer, PathGeometry clip, Rect bounds, double scale, double tolerance)
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subpaths =
            PathGeometryAccessors.Flatten(clip, tolerance);

        for (int y = 0; y < buffer.Height; y++)
        {
            double py = bounds.Y + bounds.Height - ((y + 0.5) / scale);
            for (int x = 0; x < buffer.Width; x++)
            {
                double px = bounds.X + ((x + 0.5) / scale);
                if (!PointInSubpaths(subpaths, px, py))
                {
                    buffer.SetPixelBgra(x, y, 0, 0, 0, 0);
                }
            }
        }
    }

    // Non-zero winding point-in-path over pre-flattened subpaths. Mirrors
    // PathGeometryAccessors.Contains so the mask matches the renderer's fills.
    private static bool PointInSubpaths(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> subpaths, double x, double y)
    {
        int winding = 0;
        foreach (IReadOnlyList<(double X, double Y)> subpath in subpaths)
        {
            int n = subpath.Count;
            if (n < 2)
            {
                continue;
            }

            for (int i = 0; i < n; i++)
            {
                (double X, double Y) a = subpath[i];
                (double X, double Y) b = subpath[(i + 1) % n];
                if ((a.Y > y) != (b.Y > y))
                {
                    double t = (y - a.Y) / (b.Y - a.Y);
                    double crossX = a.X + (t * (b.X - a.X));
                    if (crossX > x)
                    {
                        winding += b.Y > a.Y ? 1 : -1;
                    }
                }
            }
        }

        return winding != 0;
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
    /// <param name="ambientClip">
    /// A device-space clip inherited from an enclosing form XObject (the clip
    /// active at its <c>Do</c>), intersected with each op's own clips. Null at
    /// the top level, where no outer clip constrains the page content.
    /// </param>
    private void PaintDisplayList(
        PageDisplayList list, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip = null)
    {
        foreach (RenderOp op in list.Ops)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            switch (op)
            {
                case FillPathOp fp:
                    PaintFillOp(fp, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case StrokePathOp sp:
                    PaintStrokeOp(sp, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case DrawGlyphOp gp:
                    PaintGlyphOp(gp, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case DrawImageOp ip:
                    PaintImageOp(ip, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case NestedDisplayListOp np:
                    PaintNestedOp(np, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case ShadeOp sh:
                    PaintShadeOp(sh, buffer, pageHeight, outerTransform, ambientClip);
                    break;
                case TilingFillOp tf:
                    PaintTilingFillOp(tf, buffer, pageHeight, outerTransform, ambientClip);
                    break;
            }
        }
    }

    // Largest pattern-tile bitmap side, in pixels; finer resolution than this
    // per cell is not visible once the cell repeats thousands of times.
    private const int MaxTileBitmapSide = 2048;

    // Repeated pattern fills may cover at most this many times the output
    // area per rasterizer, so a hostile file with thousands of full-page
    // pattern fills cannot stall rendering; fills beyond the budget are
    // skipped, as every such fill was before 3.21.
    private const long RepeatedFillAreaBudget = 32;
    private long _repeatedFillPixels;

    // Paints a tiling pattern too fine to place tile by tile (3.21): the cell
    // is rendered once into a bitmap covering one repeat period at device
    // resolution, then every device pixel of the filled region is mapped back
    // into pattern space, wrapped by the steps, and sampled from that bitmap.
    // Cost scales with the output area, never with the tile count, and the
    // continuous wrap leaves no seams. A period smaller than about a pixel
    // collapses to the cell's average colour, which is what the eye sees.
    private void PaintTilingFillOp(
        TilingFillOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
    {
        double scale = _options.Scale;
        Transform patternToPage = op.PatternMatrix.Multiply(outerTransform);
        Transform pageToPattern;
        try
        {
            pageToPattern = patternToPage.Invert();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // Device length of one step along each pattern axis.
        double stepXDevice = Math.Sqrt(Square(patternToPage.A * op.XStep) + Square(patternToPage.B * op.XStep)) * scale;
        double stepYDevice = Math.Sqrt(Square(patternToPage.C * op.YStep) + Square(patternToPage.D * op.YStep)) * scale;
        if (!double.IsFinite(stepXDevice) || !double.IsFinite(stepYDevice))
        {
            return;
        }

        // Device-space bounds of the region, clipped to the buffer.
        PointF[] corners =
        {
            outerTransform.TransformPoint(new PointF(op.Region.X, op.Region.Y)),
            outerTransform.TransformPoint(new PointF(op.Region.Right, op.Region.Y)),
            outerTransform.TransformPoint(new PointF(op.Region.Right, op.Region.Top)),
            outerTransform.TransformPoint(new PointF(op.Region.X, op.Region.Top)),
        };
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (PointF c in corners)
        {
            double dx = c.X * scale;
            double dy = (pageHeight - c.Y) * scale;
            minX = Math.Min(minX, dx);
            maxX = Math.Max(maxX, dx);
            minY = Math.Min(minY, dy);
            maxY = Math.Max(maxY, dy);
        }

        int x0 = Math.Max(0, (int)Math.Floor(minX));
        int y0 = Math.Max(0, (int)Math.Floor(minY));
        int x1 = Math.Min(buffer.Width, (int)Math.Ceiling(maxX));
        int y1 = Math.Min(buffer.Height, (int)Math.Ceiling(maxY));
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);
        if (clip is not null && clip.IsEmpty)
        {
            return;
        }

        int regionW = x1 - x0;
        int regionH = y1 - y0;
        _repeatedFillPixels += (long)regionW * regionH;
        if (_repeatedFillPixels > RepeatedFillAreaBudget * buffer.Width * buffer.Height)
        {
            return;
        }

        bool average = stepXDevice < 2 && stepYDevice < 2;
        int tileW = average ? 16 : (int)Math.Clamp(Math.Ceiling(stepXDevice), 1, MaxTileBitmapSide);
        int tileH = average ? 16 : (int)Math.Clamp(Math.Ceiling(stepYDevice), 1, MaxTileBitmapSide);
        PixelBuffer tile = RenderPatternPeriod(op, tileW, tileH);
        if (average)
        {
            tile = AverageToSinglePixel(tile);
            tileW = 1;
            tileH = 1;
        }

        ImageFrame region = ImageFrame.Create(regionW, regionH, ImageColorFormat.Rgba32);
        for (int py = 0; py < regionH; py++)
        {
            double pageY = pageHeight - ((y0 + py + 0.5) / scale);
            for (int px = 0; px < regionW; px++)
            {
                PointF p = pageToPattern.TransformPoint(new PointF((x0 + px + 0.5) / scale, pageY));
                double fu = Wrap((p.X - op.CellOriginX) / op.XStep);
                double fv = Wrap((p.Y - op.CellOriginY) / op.YStep);
                int tx = Math.Min(tileW - 1, (int)(fu * tileW));
                int ty = Math.Min(tileH - 1, (int)((1.0 - fv) * tileH));
                (byte b, byte g, byte r, byte a) = tile.GetPixelBgra(tx, ty);
                region.Pixels.SetPixelBgra(px, py, b, g, r, a);
            }
        }

        float[]? smask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        CompositeImage(region, buffer, x0, y0, regionW, regionH, clip, 1.0, op.BlendMode, smask, buffer.Width);
    }

    // Renders one repeat period of the pattern — the window starting at the
    // cell origin, one step wide and high — into a transparent bitmap. The
    // cell is drawn at the neighbouring offsets too, so content reaching past
    // a step wraps into the window as overlapping tiles would paint it.
    private PixelBuffer RenderPatternPeriod(TilingFillOp op, int tileW, int tileH)
    {
        PdfWorkScope.ChargeMemory((long)tileW * tileH * 4);
        PixelBuffer tile = new PixelBuffer(tileW, tileH);
        PageRasterizer painter = new PageRasterizer(_objects, new RenderOptions
        {
            Dpi = 72,
            AntiAlias = _options.AntiAlias,
            GammaCorrect = _options.GammaCorrect,
            Hinting = HintingMode.Off,
        });

        double sx = tileW / op.XStep;
        double sy = tileH / op.YStep;
        int reachX = Math.Max(1, (int)Math.Ceiling(op.CellWidth / op.XStep));
        int reachY = Math.Max(1, (int)Math.Ceiling(op.CellHeight / op.YStep));
        for (int j = -reachY; j <= reachY; j++)
        {
            for (int i = -reachX; i <= reachX; i++)
            {
                Transform cellToTile = new Transform(
                    sx, 0, 0, sy,
                    ((i * op.XStep) - op.CellOriginX) * sx,
                    ((j * op.YStep) - op.CellOriginY) * sy);
                painter.PaintDisplayList(op.Cell, tile, tileH, cellToTile);
            }
        }

        return tile;
    }

    private static PixelBuffer AverageToSinglePixel(PixelBuffer tile)
    {
        double b = 0, g = 0, r = 0, a = 0;
        for (int y = 0; y < tile.Height; y++)
        {
            for (int x = 0; x < tile.Width; x++)
            {
                (byte pb, byte pg, byte pr, byte pa) = tile.GetPixelBgra(x, y);
                double w = pa / 255.0;
                b += pb * w;
                g += pg * w;
                r += pr * w;
                a += pa;
            }
        }

        int n = tile.Width * tile.Height;
        PixelBuffer one = new PixelBuffer(1, 1);
        double alphaSum = a / 255.0;
        if (alphaSum > 0)
        {
            one.SetPixelBgra(
                0, 0,
                (byte)Math.Round(b / alphaSum), (byte)Math.Round(g / alphaSum), (byte)Math.Round(r / alphaSum),
                (byte)Math.Round(a / n));
        }

        return one;
    }

    private static double Wrap(double value)
    {
        double f = value - Math.Floor(value);
        return f >= 1.0 ? 0.0 : f;
    }

    private static double Square(double value) => value * value;

    // Intersects an op's own clip with the ambient (parent-of-form) clip. Both
    // are already in device space, so the combined region is their geometric
    // intersection. A null on either side means "no constraint from that side".
    private static ClipRegion? CombineClips(ClipRegion? own, ClipRegion? ambient)
    {
        if (own is null) { return ambient; }
        if (ambient is null) { return own; }
        return own.Combine(ambient);
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
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
    {
        GraphicsPath device = UserSpacePathToDevice(op.Path, pageHeight, outerTransform);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(device);
        SnapThinAxisAlignedRects(subPaths);
        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);
        _scanline.BlendMode = op.BlendMode;
        _scanline.SoftMask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        _scanline.SoftMaskWidth = buffer.Width;
        _scanline.Fill(buffer, subPaths, op.Color, op.Rule, clip);
        _scanline.BlendMode = PdfBlendMode.Normal;
        _scanline.SoftMask = null;
    }

    // Dash lengths are in user-space points, like the line width; scale them
    // to device pixels the same way.
    private double[] ScaleDash(double[] pattern)
    {
        double[] scaled = new double[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            scaled[i] = pattern[i] * _options.Scale;
        }

        return scaled;
    }

    private void PaintStrokeOp(
        StrokePathOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
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
            DashPattern = ScaleDash(op.Style.DashPattern),
            DashOffset = op.Style.DashOffset * _options.Scale,
            Color = op.Style.Color,
        };

        List<List<PointF>> filled = _stroke.Expand(subPaths, deviceStyle);
        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);
        _scanline.BlendMode = op.BlendMode;
        _scanline.SoftMask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        _scanline.SoftMaskWidth = buffer.Width;
        _scanline.Fill(buffer, filled, op.Style.Color, FillRule.NonZeroWinding, clip);
        _scanline.BlendMode = PdfBlendMode.Normal;
        _scanline.SoftMask = null;
    }

    private void PaintGlyphOp(
        DrawGlyphOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
    {
        // The glyph outline is in PDF user space with textMatrix and CTM
        // already applied by DisplayListBuilder. Transform to device space.
        GraphicsPath device = UserSpacePathToDevice(op.Path, pageHeight, outerTransform);

        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(device);
        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);
        _scanline.BlendMode = op.BlendMode;
        _scanline.SoftMask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        _scanline.SoftMaskWidth = buffer.Width;
        _scanline.Fill(buffer, subPaths, op.Color, FillRule.NonZeroWinding, clip);
        _scanline.BlendMode = PdfBlendMode.Normal;
        _scanline.SoftMask = null;
    }

    private void PaintImageOp(
        DrawImageOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
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

        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);

        if (clip is not null && clip.IsEmpty)
        {
            return;
        }

        float[]? smask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        CompositeImage(
            op.Image, buffer, destX, destY - destH, destW, destH, clip, op.Alpha,
            op.BlendMode, smask, buffer.Width);
    }

    private void PaintNestedOp(
        NestedDisplayListOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
    {
        // Compose the form XObject's contribution: inner-space → outer-space
        // is op.CtmComposition; outer-space → page-space is outerTransform.
        Transform innerToPage = op.CtmComposition.Multiply(outerTransform);

        // The clip active at the Do (op.Clips, captured in the parent's space)
        // must constrain everything the form paints. Build it in device space
        // with the OUTER transform, fold in any ambient clip inherited from an
        // enclosing form, and pass the result down so each inner op intersects
        // it with its own clips. Without this the form content ignores the
        // page-level clip (W n) and bleeds outside it.
        ClipRegion? parentClip = CombineClips(
            BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);

        PaintDisplayList(op.Inner, buffer, pageHeight, innerToPage, parentClip);
    }

    // Paints an axial or radial shading (the sh operator). The gradient geometry
    // is in page space; each device pixel inside the active clip is mapped back
    // to page space, its parametric position computed, and the interpolated stop
    // colour written. Pixels outside the (optionally extended) domain are left
    // untouched, matching the transparent regions of an unextended shading.
    private void PaintShadeOp(
        ShadeOp op, PixelBuffer buffer,
        double pageHeight, Transform outerTransform, ClipRegion? ambientClip)
    {
        if (op.Stops.Count == 0)
        {
            return;
        }

        ClipRegion? clip = CombineClips(BuildClipRegion(op.Clips, pageHeight, outerTransform), ambientClip);
        if (clip is not null && clip.IsEmpty)
        {
            return;
        }

        float[]? smask = op.SoftMask is null
            ? null
            : GetSoftMask(op.SoftMask, buffer, pageHeight, outerTransform);
        int smW = buffer.Width;

        double scale = _options.Scale;
        Transform inverseOuter;
        try
        {
            inverseOuter = outerTransform.Invert();
        }
        catch (InvalidOperationException)
        {
            // Degenerate composition (collapsed form XObject) — nothing to paint.
            return;
        }

        // Precomputed axial axis terms.
        double axdx = op.X1 - op.X0;
        double axdy = op.Y1 - op.Y0;
        double axisLenSq = (axdx * axdx) + (axdy * axdy);

        // Precomputed radial terms.
        double cdx = op.X1 - op.X0;
        double cdy = op.Y1 - op.Y0;
        double dr = op.R1 - op.R0;
        double aQuad = (cdx * cdx) + (cdy * cdy) - (dr * dr);

        // Radial geometry kept in shading space (a non-conformal CTM): each
        // page point is mapped back before evaluating.
        Transform? toShading = null;
        if (op.ShadingToPage is Transform shadingToPage)
        {
            try
            {
                toShading = shadingToPage.Invert();
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        for (int py = 0; py < buffer.Height; py++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
            List<(double Start, double End)>? allowed = clip?.AllowedIntervals(py + 0.5);
            if (allowed is not null && allowed.Count == 0)
            {
                continue;
            }

            for (int px = 0; px < buffer.Width; px++)
            {
                if (allowed is not null && !InAnyInterval(allowed, px + 0.5))
                {
                    continue;
                }

                // Device pixel centre → outer space → page space.
                double outerX = (px + 0.5) / scale;
                double outerY = pageHeight - ((py + 0.5) / scale);
                PointF page = inverseOuter.TransformPoint(new PointF(outerX, outerY));
                if (toShading is Transform back)
                {
                    page = back.TransformPoint(page);
                }

                double t;
                bool inside = op.IsRadial
                    ? TryRadialParameter(op, page.X, page.Y, cdx, cdy, dr, aQuad, out t)
                    : TryAxialParameter(op, page.X, page.Y, axdx, axdy, axisLenSq, out t);

                if (!inside)
                {
                    continue;
                }

                ColorF color = SampleStops(op.Stops, t);
                if (op.BlendMode == PdfBlendMode.Normal && smask is null)
                {
                    buffer.SetPixelBgra(
                        px, py, ToByte(color.B), ToByte(color.G), ToByte(color.R), 255);
                }
                else
                {
                    WriteBlended(
                        buffer, px, py,
                        ColorF.FromRgb(color.R, color.G, color.B, 1f), op.BlendMode, smask, smW);
                }
            }
        }
    }

    private static bool TryAxialParameter(
        ShadeOp op, double gx, double gy,
        double axdx, double axdy, double axisLenSq, out double t)
    {
        if (axisLenSq < 1e-12)
        {
            t = 0.0;
            return true;
        }

        double s = (((gx - op.X0) * axdx) + ((gy - op.Y0) * axdy)) / axisLenSq;
        return ClampToDomain(op, s, out t);
    }

    private static bool TryRadialParameter(
        ShadeOp op, double gx, double gy,
        double cdx, double cdy, double dr, double aQuad, out double t)
    {
        double px = gx - op.X0;
        double py = gy - op.Y0;
        double bQuad = (px * cdx) + (py * cdy) + (op.R0 * dr);
        double cQuad = (px * px) + (py * py) - (op.R0 * op.R0);

        // Solve aQuad*s^2 - 2*bQuad*s + cQuad = 0 for the largest s whose circle
        // has non-negative radius and lies within the (extended) domain.
        if (Math.Abs(aQuad) < 1e-9)
        {
            if (Math.Abs(bQuad) < 1e-12)
            {
                t = 0.0;
                return false;
            }

            double sLin = cQuad / (2.0 * bQuad);
            return AcceptRadialRoot(op, sLin, dr, out t);
        }

        double disc = (bQuad * bQuad) - (aQuad * cQuad);
        if (disc < 0.0)
        {
            t = 0.0;
            return false;
        }

        double sq = Math.Sqrt(disc);
        double sHi = (bQuad + sq) / aQuad;
        double sLo = (bQuad - sq) / aQuad;
        if (sLo > sHi)
        {
            (sHi, sLo) = (sLo, sHi);
        }

        if (AcceptRadialRoot(op, sHi, dr, out t))
        {
            return true;
        }

        return AcceptRadialRoot(op, sLo, dr, out t);
    }

    private static bool AcceptRadialRoot(ShadeOp op, double s, double dr, out double t)
    {
        // The interpolated circle radius must be non-negative.
        if (op.R0 + (s * dr) < 0.0)
        {
            t = 0.0;
            return false;
        }

        return ClampToDomain(op, s, out t);
    }

    private static bool ClampToDomain(ShadeOp op, double s, out double t)
    {
        if (s < 0.0)
        {
            if (!op.ExtendStart)
            {
                t = 0.0;
                return false;
            }

            t = 0.0;
            return true;
        }

        if (s > 1.0)
        {
            if (!op.ExtendEnd)
            {
                t = 0.0;
                return false;
            }

            t = 1.0;
            return true;
        }

        t = s;
        return true;
    }

    private static ColorF SampleStops(IReadOnlyList<GradientStop> stops, double t)
    {
        int n = stops.Count;
        if (n == 1)
        {
            return stops[0].Color;
        }

        // Stops are emitted at evenly spaced offsets i/(n-1), so the bracketing
        // pair is a direct index — no search needed.
        double pos = t * (n - 1);
        if (pos <= 0.0)
        {
            return stops[0].Color;
        }

        if (pos >= n - 1)
        {
            return stops[n - 1].Color;
        }

        int i0 = (int)pos;
        double frac = pos - i0;
        ColorF c0 = stops[i0].Color;
        ColorF c1 = stops[i0 + 1].Color;
        return ColorF.FromRgb(
            (float)(c0.R + ((c1.R - c0.R) * frac)),
            (float)(c0.G + ((c1.G - c0.G) * frac)),
            (float)(c0.B + ((c1.B - c0.B) * frac)));
    }

    private static byte ToByte(float component)
    {
        int v = (int)Math.Round(component * 255.0f);
        if (v < 0)
        {
            v = 0;
        }
        else if (v > 255)
        {
            v = 255;
        }

        return (byte)v;
    }

    // Composites a source colour into one pixel, applying a separable blend mode
    // against the current backdrop before the source-over (PDF §11.3.5). Used by
    // the direct-write paths (images, shadings); the scanline filler has its own
    // equivalent for fills/strokes/glyphs.
    private static void WriteBlended(
        PixelBuffer buffer, int x, int y, ColorF src, PdfBlendMode mode, float[]? smask, int smW)
    {
        ColorF s = src.ToRgb();
        float alpha = s.Alpha;
        if (smask is not null)
        {
            float cov = smask[(y * smW) + x];
            if (cov <= 0f)
            {
                return;
            }

            alpha *= cov;
        }

        if (mode == PdfBlendMode.Normal)
        {
            buffer.BlendPixel(x, y, ColorF.FromRgb(s.R, s.G, s.B, alpha), gammaCorrect: false);
            return;
        }

        (byte db, byte dg, byte dr, byte da) = buffer.GetPixelBgra(x, y);
        double ab = da / 255.0;
        double crR = ((1.0 - ab) * s.R) + (ab * BlendModes.Blend(mode, dr / 255.0, s.R));
        double crG = ((1.0 - ab) * s.G) + (ab * BlendModes.Blend(mode, dg / 255.0, s.G));
        double crB = ((1.0 - ab) * s.B) + (ab * BlendModes.Blend(mode, db / 255.0, s.B));
        buffer.BlendPixel(
            x, y, ColorF.FromRgb((float)crR, (float)crG, (float)crB, alpha), gammaCorrect: false);
    }

    // Returns the device-space coverage (0..1 per pixel) for a soft mask,
    // rendering its group once and caching by mask identity.
    private float[] GetSoftMask(
        RasterSoftMaskInfo mask, PixelBuffer buffer, double pageHeight, Transform outerTransform)
    {
        if (_softMaskCache.TryGetValue(mask, out float[]? cached))
        {
            return cached;
        }

        float[] coverage = RenderSoftMask(mask, buffer, pageHeight, outerTransform);
        _softMaskCache[mask] = coverage;
        return coverage;
    }

    // Renders the masking group to its own buffer and derives per-pixel coverage:
    // luminosity of the result over the backdrop, or the group's own alpha.
    private float[] RenderSoftMask(
        RasterSoftMaskInfo mask, PixelBuffer buffer, double pageHeight, Transform outerTransform)
    {
        int w = buffer.Width;
        int h = buffer.Height;
        PdfWorkScope.ChargeMemory((long)w * h * 4);
        PixelBuffer maskBuffer = new PixelBuffer(w, h);

        if (mask.IsLuminosity)
        {
            // Luminosity masks composite over an opaque backdrop (/BC, default
            // black); unpainted areas therefore read as the backdrop luminosity.
            float bd = (float)mask.Backdrop;
            maskBuffer.Clear(ColorF.FromRgb(bd, bd, bd, 1f));
        }

        Transform groupToPage = mask.Composition.Multiply(outerTransform);
        PaintDisplayList(mask.Group, maskBuffer, pageHeight, groupToPage);

        float[] coverage = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (byte b, byte g, byte r, byte a) = maskBuffer.GetPixelBgra(x, y);
                coverage[(y * w) + x] = mask.IsLuminosity
                    ? (((0.299f * r) + (0.587f * g) + (0.114f * b)) / 255f)
                    : (a / 255f);
            }
        }

        return coverage;
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
            List<List<PointF>> flattened = flattener.Flatten(device);

            if (clip.Stroke is StrokeStyle stroke)
            {
                // A stroke-outline clip (pattern-painted stroke): the region is
                // the stroke's painted area, expanded exactly as a stroke op is
                // (device width, at least one pixel).
                StrokeStyle deviceStyle = new StrokeStyle
                {
                    Width = Math.Max(1.0, stroke.Width * _options.Scale),
                    Cap = stroke.Cap,
                    Join = stroke.Join,
                    MiterLimit = stroke.MiterLimit,
                    DashPattern = ScaleDash(stroke.DashPattern),
                    DashOffset = stroke.DashOffset * _options.Scale,
                    Color = stroke.Color,
                };
                flattened = _stroke.Expand(flattened, deviceStyle);
            }

            deviceClips.Add(flattened);
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
        ClipRegion? clip, double alpha = 1.0, PdfBlendMode mode = PdfBlendMode.Normal,
        float[]? smask = null, int smW = 0)
    {
        int dstX0 = Math.Max(0, (int)Math.Round(x));
        int dstY0 = Math.Max(0, (int)Math.Round(y));
        int dstX1 = Math.Min(buffer.Width - 1, (int)Math.Round(x + w));
        int dstY1 = Math.Min(buffer.Height - 1, (int)Math.Round(y + h));

        for (int py = dstY0; py <= dstY1; py++)
        {
            PdfWorkScope.ThrowIfCancellationRequested();
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

                // Fold the constant ExtGState alpha (/ca) into the per-pixel alpha.
                int effA = alpha >= 1.0 ? sa : (int)((sa * alpha) + 0.5);
                if (effA <= 0)
                {
                    continue;
                }

                if (mode != PdfBlendMode.Normal || smask is not null)
                {
                    WriteBlended(
                        buffer, px, py, ColorF.FromRgb8(sr, sg, sb, (byte)effA), mode, smask, smW);
                }
                else if (effA >= 255)
                {
                    buffer.SetPixelBgra(px, py, sb, sg, sr, 255);
                }
                else
                {
                    buffer.BlendPixel(px, py, ColorF.FromRgb8(sr, sg, sb, (byte)effA), gammaCorrect: false);
                }
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
