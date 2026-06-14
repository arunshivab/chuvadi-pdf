// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — image encoding for SVG embedding

using System;
using System.IO;
using System.IO.Compression;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Svg;

/// <summary>Builds data: URLs for an <see cref="ImageOp"/>.</summary>
internal static class ImageEncoder
{
    internal static string? BuildDataUrl(ImageOp op)
    {
        if (op.Format == ImageFormat.Jpeg)
        {
            return "data:image/jpeg;base64," + Convert.ToBase64String(op.PixelData);
        }
        if (op.Format == ImageFormat.Png)
        {
            return "data:image/png;base64," + Convert.ToBase64String(op.PixelData);
        }

        // Raw → PNG (CMYK is converted to RGB first).
        byte[] rgb = op.PixelData;
        int channels = op.ColorSpace switch
        {
            PdfColorSpace.DeviceGray => 1,
            PdfColorSpace.DeviceRgb => 3,
            PdfColorSpace.DeviceCmyk => 4,
            _ => 3,
        };
        if (channels == 4)
        {
            rgb = CmykToRgb(op.PixelData, op.Width, op.Height);
            channels = 3;
        }
        if (op.BitsPerComponent != 8) { return null; }

        // When the image carries a soft mask, attach it as the alpha channel and
        // emit an RGBA PNG so transparent regions are transparent rather than the
        // conventional black of the unmasked colour data.
        if (op.SoftMaskAlpha is not null)
        {
            byte[]? rgba = BuildRgba(
                rgb, channels, op.Width, op.Height,
                op.SoftMaskAlpha, op.SoftMaskWidth, op.SoftMaskHeight);
            if (rgba is not null)
            {
                byte[] rgbaPng = EncodePng(rgba, op.Width, op.Height, 4);
                return "data:image/png;base64," + Convert.ToBase64String(rgbaPng);
            }
        }

        byte[] png = EncodePng(rgb, op.Width, op.Height, channels);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    // Expands a 1- or 3-channel base image to RGBA using the soft-mask samples as
    // alpha. The mask is nearest-neighbour resampled when its size differs from
    // the base. Returns null if the base buffer is too small to interpret.
    private static byte[]? BuildRgba(
        byte[] basePixels,
        int channels,
        int width,
        int height,
        byte[] alpha,
        int alphaWidth,
        int alphaHeight)
    {
        long pixelCount = (long)width * height;
        if (channels != 1 && channels != 3) { return null; }
        if (basePixels.Length < pixelCount * channels) { return null; }

        byte[] rgba = new byte[pixelCount * 4];
        bool sameSize = alphaWidth == width && alphaHeight == height && alphaWidth > 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                byte r;
                byte g;
                byte b;

                if (channels == 1)
                {
                    byte v = basePixels[index];
                    r = v;
                    g = v;
                    b = v;
                }
                else
                {
                    r = basePixels[(index * 3) + 0];
                    g = basePixels[(index * 3) + 1];
                    b = basePixels[(index * 3) + 2];
                }

                byte a;
                if (sameSize)
                {
                    a = index < alpha.Length ? alpha[index] : (byte)255;
                }
                else if (alphaWidth > 0 && alphaHeight > 0)
                {
                    int ax = (int)((long)x * alphaWidth / width);
                    int ay = (int)((long)y * alphaHeight / height);
                    int ai = (ay * alphaWidth) + ax;
                    a = ai >= 0 && ai < alpha.Length ? alpha[ai] : (byte)255;
                }
                else
                {
                    a = 255;
                }

                int outIndex = index * 4;
                rgba[outIndex + 0] = r;
                rgba[outIndex + 1] = g;
                rgba[outIndex + 2] = b;
                rgba[outIndex + 3] = a;
            }
        }

        return rgba;
    }

    private static byte[] CmykToRgb(byte[] cmyk, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int n = width * height;
        for (int i = 0; i < n; i++)
        {
            double c = cmyk[i * 4] / 255.0;
            double m = cmyk[i * 4 + 1] / 255.0;
            double y = cmyk[i * 4 + 2] / 255.0;
            double k = cmyk[i * 4 + 3] / 255.0;
            rgb[i * 3] = (byte)((1 - c) * (1 - k) * 255);
            rgb[i * 3 + 1] = (byte)((1 - m) * (1 - k) * 255);
            rgb[i * 3 + 2] = (byte)((1 - y) * (1 - k) * 255);
        }
        return rgb;
    }

    private static byte[] EncodePng(byte[] pixels, int width, int height, int channels)
    {
        using MemoryStream ms = new();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        byte[] ihdr = new byte[13];
        WriteInt32BigEndian(ihdr, 0, width);
        WriteInt32BigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(channels switch { 1 => 0, 3 => 2, 4 => 6, _ => 2 });
        WriteChunk(ms, "IHDR", ihdr);
        using MemoryStream filtered = new();
        int stride = width * channels;
        for (int row = 0; row < height; row++)
        {
            filtered.WriteByte(0);
            filtered.Write(pixels, row * stride, stride);
        }
        byte[] deflated = Deflate(filtered.ToArray());
        WriteChunk(ms, "IDAT", deflated);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        byte[] lenBytes = new byte[4];
        WriteInt32BigEndian(lenBytes, 0, data.Length);
        s.Write(lenBytes);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteInt32BigEndian(crcBytes, 0, (int)crc);
        s.Write(crcBytes);
    }

    private static void WriteInt32BigEndian(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream ms = new();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (DeflateStream ds = new(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        const uint Mod = 65521;
        foreach (byte by in data)
        {
            a = (a + by) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        uint[] t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in type) { c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8); }
        foreach (byte b in data) { c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8); }
        return c ^ 0xFFFFFFFF;
    }
}
