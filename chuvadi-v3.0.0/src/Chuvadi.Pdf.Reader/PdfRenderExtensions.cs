// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;

namespace Chuvadi.Pdf.Reader;

/// <summary>
/// One-call rendering of PDF pages to common output formats. These extensions
/// are the simplest correct way to turn an open <see cref="PdfDocument"/> into
/// SVG, PNG, JPEG, BMP, or TIFF — open a document, call one method, get the
/// result. They wrap the full rendering pipeline (display list → renderer /
/// rasterizer → encoder), so an application never has to assemble it by hand.
/// </summary>
/// <remarks>
/// Vector output (<see cref="RenderPageToSvg(PdfDocument, int, SvgExportOptions)"/>)
/// preserves selectable text and embedded fonts. Raster output rasterizes the
/// page at a chosen DPI and encodes the pixels; 150 DPI is a good screen
/// default, 300 DPI is print quality.
/// </remarks>
public static class PdfRenderExtensions
{
    /// <summary>Renders one page to a self-contained SVG string.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="options">Optional SVG export options; defaults are used when null.</param>
    /// <returns>The page as an SVG document string.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static string RenderPageToSvg(this PdfDocument document, int pageIndex, SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePageIndex(document, pageIndex);
        return new SvgRenderer(options ?? new SvgExportOptions()).RenderPage(document, pageIndex);
    }

    /// <summary>Renders one page to SVG encoded as UTF-8 bytes.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="options">Optional SVG export options; defaults are used when null.</param>
    /// <returns>The page as UTF-8 encoded SVG bytes.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static byte[] RenderPageToSvgBytes(this PdfDocument document, int pageIndex, SvgExportOptions? options = null)
    {
        return System.Text.Encoding.UTF8.GetBytes(RenderPageToSvg(document, pageIndex, options));
    }

    /// <summary>Renders one page to PNG bytes at the given DPI.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <returns>The page encoded as a PNG image.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static byte[] RenderPageToPng(this PdfDocument document, int pageIndex, double dpi = 150)
    {
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        using MemoryStream ms = new MemoryStream();
        PngEncoder.Encode(frame, ms);
        return ms.ToArray();
    }

    /// <summary>Renders one page to PNG, writing to <paramref name="output"/>.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="output">Destination stream.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static void RenderPageToPng(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(output);
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        PngEncoder.Encode(frame, output);
    }

    /// <summary>Renders one page to JPEG bytes at the given DPI and quality.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <param name="quality">JPEG quality, 1–100. Default: 85.</param>
    /// <returns>The page encoded as a JPEG image.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static byte[] RenderPageToJpeg(this PdfDocument document, int pageIndex, double dpi = 150, int quality = 85)
    {
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        using MemoryStream ms = new MemoryStream();
        JpegEncoder.Encode(frame, ms, quality);
        return ms.ToArray();
    }

    /// <summary>Renders one page to JPEG, writing to <paramref name="output"/>.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="output">Destination stream.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <param name="quality">JPEG quality, 1–100. Default: 85.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static void RenderPageToJpeg(this PdfDocument document, int pageIndex, Stream output, double dpi = 150, int quality = 85)
    {
        ArgumentNullException.ThrowIfNull(output);
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        JpegEncoder.Encode(frame, output, quality);
    }

    /// <summary>Renders one page to BMP bytes at the given DPI.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <returns>The page encoded as a BMP image.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static byte[] RenderPageToBmp(this PdfDocument document, int pageIndex, double dpi = 150)
    {
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        using MemoryStream ms = new MemoryStream();
        BmpEncoder.Encode(frame, ms);
        return ms.ToArray();
    }

    /// <summary>Renders one page to BMP, writing to <paramref name="output"/>.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="output">Destination stream.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static void RenderPageToBmp(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(output);
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        BmpEncoder.Encode(frame, output);
    }

    /// <summary>Renders one page to a single-page TIFF at the given DPI.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <returns>The page encoded as a TIFF image.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static byte[] RenderPageToTiff(this PdfDocument document, int pageIndex, double dpi = 150)
    {
        ImageFrame frame = RenderPageToFrame(document, pageIndex, dpi);
        return TiffEncoder.Encode(frame);
    }

    /// <summary>Renders one page to TIFF, writing to <paramref name="output"/>.</summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="output">Destination stream.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="pageIndex"/> is out of range.</exception>
    public static void RenderPageToTiff(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = RenderPageToTiff(document, pageIndex, dpi);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Renders every page to a single multi-page TIFF at the given DPI.
    /// </summary>
    /// <param name="document">The open PDF document.</param>
    /// <param name="dpi">Rasterization resolution in dots per inch. Default: 150.</param>
    /// <returns>A multi-page TIFF containing one frame per page.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    public static byte[] RenderToTiff(this PdfDocument document, double dpi = 150)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<ImageFrame> frames = new List<ImageFrame>(document.PageCount);
        for (int i = 0; i < document.PageCount; i++)
        {
            frames.Add(RenderPageToFrame(document, i, dpi));
        }
        return TiffEncoder.EncodeAll(frames);
    }

    // ── Internals ─────────────────────────────────────────────────────────

    private static ImageFrame RenderPageToFrame(PdfDocument document, int pageIndex, double dpi)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePageIndex(document, pageIndex);
        PageRasterizer rasterizer = new PageRasterizer(document.Objects, new RenderOptions { Dpi = dpi });
        PixelBuffer buffer = rasterizer.Rasterize(document.Pages[pageIndex]);
        return new ImageFrame(buffer, ImageColorFormat.Rgb24);
    }

    private static void ValidatePageIndex(PdfDocument document, int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                $"Page index {pageIndex} is out of range; the document has {document.PageCount} page(s).");
        }
    }
}
