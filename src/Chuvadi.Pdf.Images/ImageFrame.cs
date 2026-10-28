// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PNG 1.2 §2 — Datastream structure; ISO 10918-1 §6 — Sequential DCT
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// A decoded image frame backed by a PixelBuffer.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Specifies the colour format of a decoded image.
/// </summary>
public enum ImageColorFormat
{
    /// <summary>8-bit grayscale (1 channel).</summary>
    Gray8,

    /// <summary>24-bit RGB (3 channels, 8 bits each).</summary>
    Rgb24,

    /// <summary>32-bit RGBA (4 channels, 8 bits each).</summary>
    Rgba32,

    /// <summary>32-bit CMYK (4 channels, 8 bits each).</summary>
    Cmyk32,
}

/// <summary>
/// A decoded image frame held in a <see cref="PixelBuffer"/>.
/// </summary>
/// <remarks>
/// <see cref="ImageFrame"/> is the output of all decoders
/// (<see cref="JpegDecoder"/>, <see cref="PngDecoder"/>)
/// and the input to all encoders
/// (<see cref="PngEncoder"/>, <see cref="BmpEncoder"/>).
///
/// The pixel data is always stored in the
/// <see cref="Chuvadi.Pdf.Graphics.PixelBuffer"/> BGRA format
/// regardless of the original image colour space.
/// The <see cref="OriginalFormat"/> property records what the
/// source image looked like before conversion.
/// </remarks>
public sealed class ImageFrame
{
    /// <summary>
    /// Initialises an <see cref="ImageFrame"/> from an existing buffer.
    /// </summary>
    public ImageFrame(PixelBuffer pixels, ImageColorFormat originalFormat)
    {
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        OriginalFormat = originalFormat;
    }

    /// <summary>Gets the pixel data in BGRA format.</summary>
    public PixelBuffer Pixels { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width => Pixels.Width;

    /// <summary>Gets the height in pixels.</summary>
    public int Height => Pixels.Height;

    /// <summary>Gets the colour format of the source image before conversion.</summary>
    public ImageColorFormat OriginalFormat { get; }

    /// <summary>
    /// Creates a new <see cref="ImageFrame"/> of the given dimensions,
    /// cleared to opaque white.
    /// </summary>
    public static ImageFrame Create(int width, int height, ImageColorFormat format)
    {
        Chuvadi.Pdf.Primitives.PdfWorkScope.ChargeMemory((long)Math.Max(0, width) * Math.Max(0, height) * 4);
        PixelBuffer buffer = new PixelBuffer(width, height);
        buffer.ClearWhite();
        return new ImageFrame(buffer, format);
    }
}
