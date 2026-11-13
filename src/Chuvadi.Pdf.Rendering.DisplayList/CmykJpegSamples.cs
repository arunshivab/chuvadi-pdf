// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5.2 (Decode arrays), §7.4.8 (DCTDecode)
// PHASE: 3.34.0 (Phase B N3) — a CMYK JPEG in a PDF is read from its stored
//        channel values through the image's /Decode array, as Poppler and
//        MuPDF read it. Passed through as a JPEG, the SVG and WPF viewers
//        applied the Adobe inverted-channel rule and ignored /Decode, so a
//        PDF that does not invert drew as a negative.

using System;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Reads the CMYK samples of a four-component JPEG image op.</summary>
internal static class CmykJpegSamples
{
    /// <summary>
    /// The op's C, M, Y, K samples (one byte each per pixel) with its
    /// <c>/Decode</c> array applied, or null when the op is not a CMYK JPEG,
    /// cannot be decoded here, or its size does not match the op's.
    /// </summary>
    internal static byte[]? Read(ImageOp op)
    {
        if (!op.IsCmykJpeg)
        {
            return null;
        }

        byte[]? cmyk;
        int width;
        int height;
        try
        {
            cmyk = JpegDecoder.DecodeCmykSamples(op.PixelData, out width, out height);
        }
        catch (ImageException)
        {
            return null;
        }

        if (cmyk is null || width != op.Width || height != op.Height)
        {
            return null;
        }

        ApplyDecode(cmyk, op.Decode);
        return cmyk;
    }

    // Each channel c maps to dmin + c * (dmax - dmin) / 255 (§8.9.5.2).
    private static void ApplyDecode(byte[] cmyk, double[]? decode)
    {
        if (decode is null || decode.Length < 8)
        {
            return;
        }

        byte[][] maps = new byte[4][];
        bool identity = true;
        for (int ch = 0; ch < 4; ch++)
        {
            double dmin = decode[2 * ch];
            double dmax = decode[(2 * ch) + 1];
            identity &= dmin == 0.0 && dmax == 1.0;
            maps[ch] = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                double mapped = dmin + (v * (dmax - dmin) / 255.0);
                maps[ch][v] = (byte)Math.Clamp((int)Math.Round(mapped * 255.0), 0, 255);
            }
        }

        if (identity)
        {
            return;
        }

        for (int i = 0; i < cmyk.Length; i++)
        {
            cmyk[i] = maps[i & 3][cmyk[i]];
        }
    }
}
