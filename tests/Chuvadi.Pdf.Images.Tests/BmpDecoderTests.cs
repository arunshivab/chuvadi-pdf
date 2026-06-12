// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Windows Bitmap (BMP) file format
// PHASE: Phase 2.7 — Image → PDF (BmpDecoder tests)

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

public sealed class BmpDecoderTests
{
    [Fact]
    public void Decode_NullData_Throws()
    {
        Action act = () => BmpDecoder.Decode((byte[])null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_NullStream_Throws()
    {
        Action act = () => BmpDecoder.Decode((Stream)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_NotABmp_ThrowsImageException()
    {
        byte[] notBmp = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05];
        Action act = () => BmpDecoder.Decode(notBmp);
        act.Should().Throw<ImageException>();
    }

    [Fact]
    public void Decode_RoundTripsThroughBmpEncoder()
    {
        ImageFrame original = ImageFrame.Create(3, 2, ImageColorFormat.Rgb24);
        original.Pixels.SetPixelBgra(0, 0, 10, 20, 30, 255);
        original.Pixels.SetPixelBgra(1, 0, 40, 50, 60, 255);
        original.Pixels.SetPixelBgra(2, 0, 70, 80, 90, 255);
        original.Pixels.SetPixelBgra(0, 1, 100, 110, 120, 255);
        original.Pixels.SetPixelBgra(1, 1, 130, 140, 150, 255);
        original.Pixels.SetPixelBgra(2, 1, 160, 170, 180, 255);

        using MemoryStream ms = new();
        BmpEncoder.Encode(original, ms);
        ImageFrame decoded = BmpDecoder.Decode(ms.ToArray());

        decoded.Width.Should().Be(3);
        decoded.Height.Should().Be(2);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                decoded.Pixels.GetPixelBgra(x, y).Should().Be(original.Pixels.GetPixelBgra(x, y));
            }
        }
    }

    [Fact]
    public void Decode_EightBitPalette_ResolvesColors()
    {
        // 2x2 8-bit palette BMP: palette[0] = red, palette[1] = blue.
        byte[] bmp = BuildPalettedBmp(
            width: 2, height: 2, bpp: 8,
            palette: new List<byte[]> { new byte[] { 0, 0, 255 }, new byte[] { 255, 0, 0 } },
            // Bottom-up rows, each padded to 4 bytes: bottom row [0,1], top row [1,0].
            pixelRows: new List<byte[]> { new byte[] { 0, 1, 0, 0 }, new byte[] { 1, 0, 0, 0 } });

        ImageFrame frame = BmpDecoder.Decode(bmp);

        frame.Width.Should().Be(2);
        frame.Height.Should().Be(2);
        // First file row is the bottom image row.
        frame.Pixels.GetPixelBgra(0, 1).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
        frame.Pixels.GetPixelBgra(1, 1).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        frame.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        frame.Pixels.GetPixelBgra(1, 0).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
    }

    [Fact]
    public void Decode_OneBitPalette_ResolvesColors()
    {
        // 8x1 1-bit BMP: bit pattern 1010 0001 over black/white palette.
        byte[] bmp = BuildPalettedBmp(
            width: 8, height: 1, bpp: 1,
            palette: new List<byte[]> { new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 } },
            pixelRows: new List<byte[]> { new byte[] { 0xA1, 0, 0, 0 } });

        ImageFrame frame = BmpDecoder.Decode(bmp);

        frame.Pixels.GetPixelBgra(0, 0).A.Should().Be(255);
        frame.Pixels.GetPixelBgra(0, 0).R.Should().Be(255);
        frame.Pixels.GetPixelBgra(1, 0).R.Should().Be(0);
        frame.Pixels.GetPixelBgra(7, 0).R.Should().Be(255);
    }

    [Fact]
    public void Decode_TopDown_PreservesRowOrder()
    {
        // 1x2 24-bit top-down (negative height): first file row is the TOP row.
        byte[] bmp = Build24BitBmp(
            width: 1, height: 2, topDown: true,
            pixelRows: new List<byte[]>
            {
                new byte[] { 1, 2, 3, 0 },
                new byte[] { 4, 5, 6, 0 },
            });

        ImageFrame frame = BmpDecoder.Decode(bmp);

        frame.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)1, (byte)2, (byte)3, (byte)255));
        frame.Pixels.GetPixelBgra(0, 1).Should().Be(((byte)4, (byte)5, (byte)6, (byte)255));
    }

    [Fact]
    public void Decode_Rle8_ExpandsRuns()
    {
        // 4x1 RLE8: encoded run of 4 pixels of palette index 1, then end-of-bitmap.
        List<byte> data = new();
        data.AddRange(new byte[] { 4, 1 });   // run: 4 × index 1
        data.AddRange(new byte[] { 0, 1 });   // end of bitmap

        byte[] bmp = BuildBmp(
            width: 4, height: 1, bpp: 8, compression: 1,
            palette: new List<byte[]> { new byte[] { 9, 9, 9 }, new byte[] { 20, 40, 60 } },
            pixelData: data.ToArray());

        ImageFrame frame = BmpDecoder.Decode(bmp);

        for (int x = 0; x < 4; x++)
        {
            frame.Pixels.GetPixelBgra(x, 0).Should().Be(((byte)20, (byte)40, (byte)60, (byte)255));
        }
    }

    [Fact]
    public void Decode_BitFieldsWithAlphaMask_PreservesAlpha()
    {
        // 1x1 32-bit BI_BITFIELDS (V4 header) with an alpha mask; pixel ARGB = 0x80FF0000.
        byte[] bmp = Build32BitFieldsBmp(a: 0x80, r: 0xFF, g: 0x00, b: 0x00);

        ImageFrame frame = BmpDecoder.Decode(bmp);

        frame.OriginalFormat.Should().Be(ImageColorFormat.Rgba32);
        (byte bOut, byte gOut, byte rOut, byte aOut) = frame.Pixels.GetPixelBgra(0, 0);
        bOut.Should().Be(0);
        gOut.Should().Be(0);
        rOut.Should().Be(255);
        aOut.Should().Be(0x80);
    }

    // ── BMP construction helpers ──────────────────────────────────────────

    private static byte[] BuildPalettedBmp(
        int width, int height, int bpp, List<byte[]> palette, List<byte[]> pixelRows)
    {
        List<byte> data = new();
        foreach (byte[] row in pixelRows)
        {
            data.AddRange(row);
        }
        return BuildBmp(width, height, bpp, compression: 0, palette, data.ToArray());
    }

    private static byte[] Build24BitBmp(
        int width, int height, bool topDown, List<byte[]> pixelRows)
    {
        List<byte> data = new();
        foreach (byte[] row in pixelRows)
        {
            data.AddRange(row);
        }
        return BuildBmp(width, topDown ? -height : height, 24, compression: 0,
            palette: null, pixelData: data.ToArray());
    }

    private static byte[] BuildBmp(
        int width, int height, int bpp, int compression,
        List<byte[]>? palette, byte[] pixelData)
    {
        List<byte> b = new();
        int paletteBytes = (palette?.Count ?? 0) * 4;
        int dataOffset = 14 + 40 + paletteBytes;

        // BITMAPFILEHEADER.
        b.Add((byte)'B');
        b.Add((byte)'M');
        AddU32(b, (uint)(dataOffset + pixelData.Length));
        AddU32(b, 0);
        AddU32(b, (uint)dataOffset);

        // BITMAPINFOHEADER.
        AddU32(b, 40);
        AddI32(b, width);
        AddI32(b, height);
        AddU16(b, 1);
        AddU16(b, (ushort)bpp);
        AddU32(b, (uint)compression);
        AddU32(b, (uint)pixelData.Length);
        AddU32(b, 2835);
        AddU32(b, 2835);
        AddU32(b, (uint)(palette?.Count ?? 0));
        AddU32(b, 0);

        if (palette is not null)
        {
            foreach (byte[] entry in palette)
            {
                b.Add(entry[0]);
                b.Add(entry[1]);
                b.Add(entry[2]);
                b.Add(0);
            }
        }

        b.AddRange(pixelData);
        return b.ToArray();
    }

    private static byte[] Build32BitFieldsBmp(byte a, byte r, byte g, byte b)
    {
        List<byte> o = new();
        int dataOffset = 14 + 108;

        o.Add((byte)'B');
        o.Add((byte)'M');
        AddU32(o, (uint)(dataOffset + 4));
        AddU32(o, 0);
        AddU32(o, (uint)dataOffset);

        // BITMAPV4HEADER (108 bytes).
        AddU32(o, 108);
        AddI32(o, 1);
        AddI32(o, 1);
        AddU16(o, 1);
        AddU16(o, 32);
        AddU32(o, 3);            // BI_BITFIELDS
        AddU32(o, 4);
        AddU32(o, 2835);
        AddU32(o, 2835);
        AddU32(o, 0);
        AddU32(o, 0);
        AddU32(o, 0x00FF0000);   // red mask
        AddU32(o, 0x0000FF00);   // green mask
        AddU32(o, 0x000000FF);   // blue mask
        AddU32(o, 0xFF000000);   // alpha mask
        for (int i = 0; i < 13; i++)
        {
            AddU32(o, 0);        // colour space + endpoints + gamma
        }

        // One ARGB pixel, little-endian: B, G, R, A.
        o.Add(b);
        o.Add(g);
        o.Add(r);
        o.Add(a);
        return o.ToArray();
    }

    private static void AddU16(List<byte> b, ushort v)
    {
        b.Add((byte)(v & 0xFF));
        b.Add((byte)(v >> 8));
    }

    private static void AddI32(List<byte> b, int v) => AddU32(b, (uint)v);

    private static void AddU32(List<byte> b, uint v)
    {
        b.Add((byte)(v & 0xFF));
        b.Add((byte)((v >> 8) & 0xFF));
        b.Add((byte)((v >> 16) & 0xFF));
        b.Add((byte)((v >> 24) & 0xFF));
    }
}
