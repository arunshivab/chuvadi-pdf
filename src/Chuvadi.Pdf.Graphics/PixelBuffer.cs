// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Phase 2 — rasterizer output format
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// A packed BGRA pixel buffer for rasterizer output.

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// A packed BGRA (Blue, Green, Red, Alpha) pixel buffer.
/// The rasterizer writes rendered pages into a <see cref="PixelBuffer"/>,
/// which is then encoded to PNG or BMP by Chuvadi.Pdf.Images.
/// </summary>
/// <remarks>
/// Pixel layout: 4 bytes per pixel in memory order (B, G, R, A).
/// Row order: top-to-bottom (first byte = top-left pixel's B channel).
/// This matches Windows BMP and common graphics conventions.
/// Note: PDF coordinate space is bottom-left origin; the rasterizer
/// flips Y when writing to the pixel buffer.
/// </remarks>
public sealed class PixelBuffer
{
    private readonly byte[] _pixels;

    /// <summary>
    /// Initialises a new <see cref="PixelBuffer"/> with all pixels transparent black.
    /// </summary>
    /// <param name="width">Width in pixels. Must be positive.</param>
    /// <param name="height">Height in pixels. Must be positive.</param>
    public PixelBuffer(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        Width = width;
        Height = height;
        _pixels = new byte[width * height * 4];
    }

    /// <summary>Gets the width of the buffer in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the buffer in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the total number of bytes (Width × Height × 4).</summary>
    public int ByteCount => _pixels.Length;

    /// <summary>Gets the row stride in bytes (Width × 4).</summary>
    public int Stride => Width * 4;

    /// <summary>Gets a read-only span over the raw pixel bytes.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    // ── Pixel access ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets a pixel at (x, y) from a <see cref="ColorF"/> value.
    /// Out-of-range coordinates are silently ignored.
    /// </summary>
    public void SetPixel(int x, int y, ColorF color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        int offset = (y * Width + x) * 4;
        ColorF rgb = color.ToRgb();
        _pixels[offset] = (byte)(rgb.B * 255f + 0.5f);
        _pixels[offset + 1] = (byte)(rgb.G * 255f + 0.5f);
        _pixels[offset + 2] = (byte)(rgb.R * 255f + 0.5f);
        _pixels[offset + 3] = (byte)(rgb.Alpha * 255f + 0.5f);
    }

    /// <summary>
    /// Sets a pixel at (x, y) from packed BGRA bytes.
    /// Out-of-range coordinates are silently ignored.
    /// </summary>
    public void SetPixelBgra(int x, int y, byte b, byte g, byte r, byte a)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        int offset = (y * Width + x) * 4;
        _pixels[offset] = b;
        _pixels[offset + 1] = g;
        _pixels[offset + 2] = r;
        _pixels[offset + 3] = a;
    }

    /// <summary>
    /// Gets the BGRA bytes of a pixel.
    /// Returns (0, 0, 0, 0) for out-of-range coordinates.
    /// </summary>
    public (byte B, byte G, byte R, byte A) GetPixelBgra(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return (0, 0, 0, 0);
        }

        int offset = (y * Width + x) * 4;
        return (_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]);
    }

    /// <summary>
    /// Blends a colour over the existing pixel using standard alpha compositing
    /// (Porter-Duff "over" operation) with gamma-correct (linear-light) mixing.
    /// PDF 32000-1:2008 -11.3 - Basic compositing formula.
    /// </summary>
    public void BlendPixel(int x, int y, ColorF color)
    {
        BlendPixel(x, y, color, gammaCorrect: true);
    }

    /// <summary>
    /// Blends a colour over the existing pixel using standard alpha compositing
    /// (Porter-Duff "over" operation). When <paramref name="gammaCorrect"/> is
    /// true the colour channels are mixed in linear light (sRGB decoded before
    /// and re-encoded after the blend), which gives anti-aliased edges their
    /// correct perceptual weight; when false the channels are mixed directly in
    /// sRGB space (the legacy behaviour).
    /// PDF 32000-1:2008 -11.3 - Basic compositing formula.
    /// </summary>
    /// <param name="x">Destination pixel X coordinate.</param>
    /// <param name="y">Destination pixel Y coordinate.</param>
    /// <param name="color">Source colour to blend over the destination.</param>
    /// <param name="gammaCorrect">Whether to mix channels in linear light.</param>
    public void BlendPixel(int x, int y, ColorF color, bool gammaCorrect)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        ColorF rgb = color.ToRgb();
        float srcA = rgb.Alpha;

        if (srcA <= 0f)
        {
            return;
        }

        if (srcA >= 1f)
        {
            SetPixel(x, y, color);
            return;
        }

        int offset = (y * Width + x) * 4;
        float dstA = _pixels[offset + 3] / 255f;
        float outA = srcA + dstA * (1f - srcA);

        if (outA <= 0f)
        {
            return;
        }

        float invOutA = 1f / outA;
        float weight = dstA * (1f - srcA);
        float outR;
        float outG;
        float outB;

        if (gammaCorrect)
        {
            float srcRl = Srgb.ToLinear(rgb.R);
            float srcGl = Srgb.ToLinear(rgb.G);
            float srcBl = Srgb.ToLinear(rgb.B);
            float dstRl = Srgb.ByteToLinear(_pixels[offset + 2]);
            float dstGl = Srgb.ByteToLinear(_pixels[offset + 1]);
            float dstBl = Srgb.ByteToLinear(_pixels[offset]);

            float outRl = ((srcRl * srcA) + (dstRl * weight)) * invOutA;
            float outGl = ((srcGl * srcA) + (dstGl * weight)) * invOutA;
            float outBl = ((srcBl * srcA) + (dstBl * weight)) * invOutA;

            outR = Srgb.ToSrgb(outRl);
            outG = Srgb.ToSrgb(outGl);
            outB = Srgb.ToSrgb(outBl);
        }
        else
        {
            outR = ((rgb.R * srcA) + ((_pixels[offset + 2] / 255f) * weight)) * invOutA;
            outG = ((rgb.G * srcA) + ((_pixels[offset + 1] / 255f) * weight)) * invOutA;
            outB = ((rgb.B * srcA) + ((_pixels[offset] / 255f) * weight)) * invOutA;
        }

        _pixels[offset] = (byte)((outB * 255f) + 0.5f);
        _pixels[offset + 1] = (byte)((outG * 255f) + 0.5f);
        _pixels[offset + 2] = (byte)((outR * 255f) + 0.5f);
        _pixels[offset + 3] = (byte)((outA * 255f) + 0.5f);
    }

    /// <summary>Fills the entire buffer with the given colour.</summary>
    public void Clear(ColorF color)
    {
        ColorF rgb = color.ToRgb();
        byte b = (byte)(rgb.B * 255f + 0.5f);
        byte g = (byte)(rgb.G * 255f + 0.5f);
        byte r = (byte)(rgb.R * 255f + 0.5f);
        byte a = (byte)(rgb.Alpha * 255f + 0.5f);

        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = b;
            _pixels[i + 1] = g;
            _pixels[i + 2] = r;
            _pixels[i + 3] = a;
        }
    }

    /// <summary>Fills the entire buffer with opaque white.</summary>
    public void ClearWhite()
    {
        Clear(ColorF.White);
    }

    /// <summary>Gets a row of pixels as a byte span (for efficient encoding).</summary>
    public ReadOnlySpan<byte> GetRow(int y)
    {
        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return _pixels.AsSpan(y * Stride, Stride);
    }
}
