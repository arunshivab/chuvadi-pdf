// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images
// PHASE: 3.35.0 (D-391 I1) — the pixels of a display-list image, exactly as
//        the raster route paints them.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Rendering.Raster;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Decodes a display-list <see cref="ImageOp"/> into the pixels the raster
/// route paints for it, through the same decoder the raster renderer uses, so
/// the two outputs stay identical.
/// </summary>
public static class ImageDecoding
{
    /// <summary>
    /// The pixels of <paramref name="image"/> as the raster route paints them:
    /// 8-bit straight (not premultiplied) RGBA in sRGB, top row first, in the
    /// orientation of the raster route's image frame. Every colour space,
    /// bit depth, <c>/Decode</c>, <c>/SMask</c> (on JPEGs too), <c>/Mask</c>
    /// (stencil and colour key) and stencil mask the raster route draws is
    /// covered; a stencil mask takes the op's
    /// <see cref="ImageOp.StencilColor"/>. When the raster route would not
    /// draw the image (a 16-bit sample, Lab, Separation, DeviceN, JPEG 2000
    /// and so on), the result has no pixels and the same reason text the
    /// raster route reports.
    /// </summary>
    /// <param name="image">An image op from a display list built by <see cref="Chuvadi.Pdf.Rendering.DisplayList.DisplayListBuilder"/>.</param>
    /// <returns>The decoded image, or the reason it is not drawn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
    public static DecodedImage Decode(ImageOp image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.DecodeSource is not PdfStream stream || image.DecodeObjects is null)
        {
            return DecodedImage.Skipped("The image op was not built from a PDF image, so there is nothing to decode.");
        }

        List<string> reasons = new List<string>();
        ColorF stencil = StencilFill(image.StencilColor);
        ImagePixelDecoder decoder = new ImagePixelDecoder(
            image.DecodeObjects,
            (kind, _, reason) =>
            {
                if (kind is PdfWarningKind.ImageSkipped or PdfWarningKind.StreamDamaged)
                {
                    reasons.Add(reason);
                }
            },
            stencil);
        ImageFrame? frame;
        try
        {
            frame = decoder.Decode(stream, "image");
        }
        catch (Exception ex) when (!PdfWorkScope.IsAbort(ex))
        {
            return DecodedImage.Skipped("The image could not be decoded (" + ex.Message + "); the image is skipped.");
        }

        if (frame is null)
        {
            return DecodedImage.Skipped(reasons.Count > 0 ? reasons[^1] : "The image is not drawn.");
        }

        return DecodedImage.From(frame, CanPassThroughJpeg(image));
    }

    // A 1- or 3-component JPEG with no /SMask, no /Mask and a default or
    // absent /Decode can be passed on as its original bytes.
    private static bool CanPassThroughJpeg(ImageOp image)
    {
        if (image.Format != ImageFormat.Jpeg || image.IsStencilMask || image.JpegComponents is not (1 or 3)
            || image.DecodeSource is not PdfStream stream)
        {
            return false;
        }

        PdfDictionary dict = stream.Dictionary;
        if (dict.ContainsKey(PdfName.Intern("SMask")) || dict.ContainsKey(PdfName.Intern("Mask")))
        {
            return false;
        }

        if (image.Decode is not double[] decode)
        {
            return true;
        }

        for (int i = 0; i < decode.Length; i++)
        {
            if (decode[i] != (i % 2 == 0 ? 0.0 : 1.0))
            {
                return false;
            }
        }

        return true;
    }

    // The stencil colour as the raster route holds the fill colour.
    private static ColorF StencilFill(PdfColor color) => color.Space switch
    {
        PdfColorSpace.DeviceRgb => ColorF.FromRgb((float)color.C0, (float)color.C1, (float)color.C2),
        PdfColorSpace.DeviceCmyk => ColorF.FromCmyk((float)color.C0, (float)color.C1, (float)color.C2, (float)color.C3),
        _ => ColorF.FromGray((float)color.C0),
    };
}

/// <summary>
/// The result of <see cref="ImageDecoding.Decode(ImageOp)"/>: the pixels the
/// raster route paints for an image, or the reason it paints none.
/// </summary>
public sealed class DecodedImage
{
    private DecodedImage(int width, int height, byte[]? pixels, bool hasAlpha, bool canPassThroughJpeg, string? skipReason)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        HasAlpha = hasAlpha;
        CanPassThroughJpeg = canPassThroughJpeg;
        SkipReason = skipReason;
    }

    /// <summary>The width in pixels; 0 when the image is not drawn.</summary>
    public int Width { get; }

    /// <summary>The height in pixels; 0 when the image is not drawn.</summary>
    public int Height { get; }

    /// <summary>
    /// The pixels as 8-bit straight (not premultiplied) RGBA in sRGB, four
    /// bytes per pixel, top row first; null when the image is not drawn.
    /// </summary>
    public byte[]? Pixels { get; }

    /// <summary>True when any pixel is less than opaque (after /SMask, /Mask and stencils).</summary>
    public bool HasAlpha { get; }

    /// <summary>
    /// True for a 1- or 3-component JPEG with no <c>/SMask</c>, no
    /// <c>/Mask</c> and a default or absent <c>/Decode</c>: the original JPEG
    /// bytes (<see cref="ImageOp.PixelData"/>) show the same image and can be
    /// kept.
    /// </summary>
    public bool CanPassThroughJpeg { get; }

    /// <summary>
    /// Why the raster route does not draw the image (the text it reports), or
    /// null when <see cref="Pixels"/> is set.
    /// </summary>
    public string? SkipReason { get; }

    internal static DecodedImage Skipped(string reason) => new DecodedImage(0, 0, null, false, false, reason);

    internal static DecodedImage From(ImageFrame frame, bool canPassThroughJpeg)
    {
        int width = frame.Width;
        int height = frame.Height;
        byte[] rgba = new byte[(long)width * height * 4];
        bool hasAlpha = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte b, byte g, byte r, byte a) = frame.Pixels.GetPixelBgra(x, y);
                long o = (((long)y * width) + x) * 4;
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = a;
                hasAlpha |= a < 255;
            }
        }

        return new DecodedImage(width, height, rgba, hasAlpha, canPassThroughJpeg, null);
    }
}
