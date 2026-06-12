// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images
// PHASE: Phase 2.7 — Image → PDF
// One-call conversion of image files (JPEG, PNG, TIFF, BMP) to PDF documents.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// How <see cref="ImagePdfConverter"/> sizes each PDF page relative to its image.
/// </summary>
public enum ImagePageSizing
{
    /// <summary>
    /// The page is exactly the image's size at <see cref="ImagePdfOptions.Dpi"/>
    /// (page points = pixels × 72 ÷ DPI). No margins; the image fills the page.
    /// </summary>
    SizeToImage = 0,

    /// <summary>
    /// The page is a fixed paper size (<see cref="ImagePdfOptions.PageSize"/>);
    /// the image is scaled to fit inside the margins, preserving aspect ratio.
    /// </summary>
    FitToPage = 1,
}

/// <summary>
/// Options for <see cref="ImagePdfConverter"/>.
/// </summary>
public sealed class ImagePdfOptions
{
    /// <summary>Default options: page sized to the image at 96 DPI.</summary>
    public static ImagePdfOptions Default { get; } = new ImagePdfOptions();

    /// <summary>Gets or initialises the page sizing strategy. Default: <see cref="ImagePageSizing.SizeToImage"/>.</summary>
    public ImagePageSizing Sizing { get; init; } = ImagePageSizing.SizeToImage;

    /// <summary>
    /// Gets or initialises the resolution, in pixels per inch, used to convert
    /// image pixels to page points. Default: 96.
    /// </summary>
    public double Dpi { get; init; } = 96;

    /// <summary>
    /// Gets or initialises the paper size used by <see cref="ImagePageSizing.FitToPage"/>.
    /// Default: A4. Use <see cref="PageSize.Landscape"/> for landscape orientation.
    /// </summary>
    public PageSize PageSize { get; init; } = PageSize.A4;

    /// <summary>
    /// Gets or initialises the page margin in points, applied on all four sides
    /// under <see cref="ImagePageSizing.FitToPage"/>. Default: 36 (half an inch).
    /// </summary>
    public double Margin { get; init; } = 36;

    /// <summary>
    /// Gets or initialises whether the image is centred inside the content area
    /// under <see cref="ImagePageSizing.FitToPage"/>; when false the image is
    /// placed at the top-left margin corner. Default: true.
    /// </summary>
    public bool CenterOnPage { get; init; } = true;

    /// <summary>
    /// Gets or initialises whether an image smaller than the content area is
    /// scaled up to fill it under <see cref="ImagePageSizing.FitToPage"/>.
    /// When false (the default) small images render at their natural
    /// <see cref="Dpi"/>-derived size.
    /// </summary>
    public bool UpscaleSmallImages { get; init; }

    /// <summary>
    /// Gets or initialises whether a multi-frame TIFF expands to one PDF page
    /// per frame. When false only the first frame converts. Default: true.
    /// </summary>
    public bool ExpandTiffFrames { get; init; } = true;

    /// <summary>Gets or initialises the document's /Title metadata.</summary>
    public string? Title { get; init; }

    /// <summary>Gets or initialises the document's /Author metadata.</summary>
    public string? Author { get; init; }
}

/// <summary>
/// Converts images (JPEG, PNG, TIFF, BMP) into PDF documents — one page per
/// image (and, optionally, one page per TIFF frame).
/// </summary>
/// <remarks>
/// <para>
/// The converter is a thin layer over <see cref="PdfDocumentBuilder"/>:
/// each image becomes a page whose size and placement follow
/// <see cref="ImagePdfOptions"/>. Baseline JPEG and 8-bit truecolour PNG embed
/// without recompression; other formats are decoded by the
/// <c>Chuvadi.Pdf.Images</c> codecs and embedded as Flate-compressed samples.
/// Alpha channels are preserved via PDF soft masks.
/// </para>
/// </remarks>
public static class ImagePdfConverter
{
    /// <summary>Converts a single image to a single-page PDF (or one page per TIFF frame).</summary>
    /// <param name="image">The encoded image bytes (JPEG, PNG, TIFF, or BMP).</param>
    /// <param name="options">Conversion options; null uses <see cref="ImagePdfOptions.Default"/>.</param>
    /// <returns>The PDF file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentException">The bytes are not a recognised image format.</exception>
    public static byte[] Convert(byte[] image, ImagePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Convert(new[] { image }, options);
    }

    /// <summary>Converts several images to a multi-page PDF, one page per image.</summary>
    /// <param name="images">The encoded images, in page order.</param>
    /// <param name="options">Conversion options; null uses <see cref="ImagePdfOptions.Default"/>.</param>
    /// <returns>The PDF file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="images"/> is null.</exception>
    /// <exception cref="ArgumentException">No images were supplied, or a format was not recognised.</exception>
    public static byte[] Convert(IReadOnlyList<byte[]> images, ImagePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("At least one image is required.", nameof(images));
        }

        ImagePdfOptions opts = options ?? ImagePdfOptions.Default;
        if (opts.Dpi <= 0)
        {
            throw new ArgumentException("Dpi must be positive.", nameof(options));
        }

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        if (opts.Title is not null)
        {
            builder.SetTitle(opts.Title);
        }
        if (opts.Author is not null)
        {
            builder.SetAuthor(opts.Author);
        }

        foreach (byte[] image in images)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(images));
            AddImagePages(builder, image, opts);
        }

        return builder.ToByteArray();
    }

    /// <summary>Converts a single image and writes the PDF to a stream.</summary>
    public static void Convert(byte[] image, Stream output, ImagePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] pdf = Convert(image, options);
        output.Write(pdf, 0, pdf.Length);
    }

    /// <summary>Converts several images and writes the PDF to a stream.</summary>
    public static void Convert(
        IReadOnlyList<byte[]> images, Stream output, ImagePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] pdf = Convert(images, options);
        output.Write(pdf, 0, pdf.Length);
    }

    /// <summary>Converts an image file on disk to a PDF file on disk.</summary>
    /// <param name="imagePath">Path of the source image (JPEG, PNG, TIFF, or BMP).</param>
    /// <param name="outputPath">Path of the PDF to create (overwritten when present).</param>
    /// <param name="options">Conversion options; null uses <see cref="ImagePdfOptions.Default"/>.</param>
    public static void ConvertFile(
        string imagePath, string outputPath, ImagePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        ArgumentNullException.ThrowIfNull(outputPath);
        byte[] pdf = Convert(File.ReadAllBytes(imagePath), options);
        File.WriteAllBytes(outputPath, pdf);
    }

    // ── Page assembly ─────────────────────────────────────────────────────

    private static void AddImagePages(
        PdfDocumentBuilder builder, byte[] image, ImagePdfOptions opts)
    {
        if (opts.ExpandTiffFrames && ImageEmbedder.Sniff(image) == ImageFormat.Tiff)
        {
            List<ImageFrame> frames = TiffDecoder.Decode(image);
            foreach (ImageFrame frame in frames)
            {
                AddFramePage(builder, frame, opts);
            }
            return;
        }

        (int w, int h) = ImageEmbedder.Measure(image);
        PageBuilder page = AddSizedPage(builder, w, h, opts, out double x, out double y,
            out double drawW, out double drawH);
        page.DrawImage(image, x, y, drawW, drawH);
    }

    private static void AddFramePage(
        PdfDocumentBuilder builder, ImageFrame frame, ImagePdfOptions opts)
    {
        PageBuilder page = AddSizedPage(builder, frame.Width, frame.Height, opts,
            out double x, out double y, out double drawW, out double drawH);
        page.DrawImage(frame, x, y, drawW, drawH);
    }

    private static PageBuilder AddSizedPage(
        PdfDocumentBuilder builder, int pixelWidth, int pixelHeight, ImagePdfOptions opts,
        out double x, out double y, out double drawWidth, out double drawHeight)
    {
        double naturalW = pixelWidth * 72.0 / opts.Dpi;
        double naturalH = pixelHeight * 72.0 / opts.Dpi;

        if (opts.Sizing == ImagePageSizing.SizeToImage)
        {
            x = 0;
            y = 0;
            drawWidth = naturalW;
            drawHeight = naturalH;
            return builder.AddPage(new PageSize(naturalW, naturalH));
        }

        // FitToPage.
        PageBuilder page = builder.AddPage(opts.PageSize);
        double areaW = Math.Max(1, opts.PageSize.Width - (opts.Margin * 2));
        double areaH = Math.Max(1, opts.PageSize.Height - (opts.Margin * 2));

        double scale = Math.Min(areaW / naturalW, areaH / naturalH);
        if (!opts.UpscaleSmallImages && scale > 1.0)
        {
            scale = 1.0;
        }

        drawWidth = naturalW * scale;
        drawHeight = naturalH * scale;
        x = opts.CenterOnPage ? opts.Margin + ((areaW - drawWidth) / 2) : opts.Margin;
        y = opts.CenterOnPage ? opts.Margin + ((areaH - drawHeight) / 2) : opts.Margin;
        return page;
    }
}
