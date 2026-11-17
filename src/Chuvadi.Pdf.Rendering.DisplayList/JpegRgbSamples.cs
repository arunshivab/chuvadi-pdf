// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.8 (DCTDecode), §11.6.5.3 (soft-mask images)
// PHASE: 3.35.0 (backlog 62) — a JPEG image with a soft mask is decoded here
//        so the SVG and WPF outputs can attach the mask as an alpha channel.

using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Decodes a JPEG image op to 8-bit RGB samples.</summary>
internal static class JpegRgbSamples
{
    /// <summary>
    /// The op's samples as 8-bit R, G, B per pixel, or null when the op is
    /// not a JPEG, cannot be decoded here, or its size does not match the
    /// op's. A CMYK JPEG is read through <see cref="CmykJpegSamples"/> and
    /// converted with the plain subtractive formula, as the outputs do.
    /// </summary>
    internal static byte[]? Read(ImageOp op)
    {
        if (op.Format != ImageFormat.Jpeg)
        {
            return null;
        }

        int pixels = op.Width * op.Height;
        if (op.IsCmykJpeg)
        {
            byte[]? cmyk = CmykJpegSamples.Read(op);
            if (cmyk is null)
            {
                return null;
            }

            byte[] fromCmyk = new byte[pixels * 3];
            for (int i = 0; i < pixels; i++)
            {
                int k = 255 - cmyk[(i * 4) + 3];
                fromCmyk[i * 3] = (byte)((255 - cmyk[i * 4]) * k / 255);
                fromCmyk[(i * 3) + 1] = (byte)((255 - cmyk[(i * 4) + 1]) * k / 255);
                fromCmyk[(i * 3) + 2] = (byte)((255 - cmyk[(i * 4) + 2]) * k / 255);
            }

            return fromCmyk;
        }

        ImageFrame frame;
        try
        {
            frame = JpegDecoder.Decode(op.PixelData);
        }
        catch (ImageException)
        {
            return null;
        }

        if (frame.Width != op.Width || frame.Height != op.Height)
        {
            return null;
        }

        PixelBuffer buffer = frame.Pixels;
        byte[] rgb = new byte[pixels * 3];
        for (int y = 0; y < op.Height; y++)
        {
            for (int x = 0; x < op.Width; x++)
            {
                (byte b, byte g, byte r, byte _) = buffer.GetPixelBgra(x, y);
                int o = ((y * op.Width) + x) * 3;
                rgb[o] = r;
                rgb[o + 1] = g;
                rgb[o + 2] = b;
            }
        }

        return rgb;
    }

    /// <summary>
    /// The gray samples of a one-component JPEG of the given size, or null
    /// when it is not one (a soft mask stored as a JPEG).
    /// </summary>
    internal static byte[]? Gray(byte[] jpeg, int width, int height)
    {
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        ImageFrame frame;
        try
        {
            frame = JpegDecoder.Decode(jpeg);
        }
        catch (ImageException)
        {
            return null;
        }

        if (frame.Width != width || frame.Height != height || frame.OriginalFormat != ImageColorFormat.Gray8)
        {
            return null;
        }

        byte[] gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                gray[(y * width) + x] = frame.Pixels.GetPixelBgra(x, y).R;
            }
        }

        return gray;
    }
}
