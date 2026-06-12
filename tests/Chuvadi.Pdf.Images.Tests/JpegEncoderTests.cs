// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.81 — Baseline sequential DCT; JFIF 1.02
// PHASE: Phase 2.9 — Reader feature batch (JPEG export) tests

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

public sealed class JpegEncoderTests
{
    private static ImageFrame SolidFrame(int width, int height, byte r, byte g, byte b)
    {
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, b, g, r, 255);
            }
        }
        return frame;
    }

    private static ImageFrame GradientFrame(int width, int height)
    {
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = (byte)(x * 255 / Math.Max(1, width - 1));
                byte g = (byte)(y * 255 / Math.Max(1, height - 1));
                byte b = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
                frame.Pixels.SetPixelBgra(x, y, b, g, r, 255);
            }
        }
        return frame;
    }

    private static byte[] Encode(ImageFrame frame, int quality = 85)
    {
        using MemoryStream ms = new();
        JpegEncoder.Encode(frame, ms, quality);
        return ms.ToArray();
    }

    [Fact]
    public void SolidColor_RoundtripsThroughOwnDecoder()
    {
        ImageFrame original = SolidFrame(32, 32, 200, 100, 50);
        byte[] jpeg = Encode(original, quality: 90);

        ImageFrame decoded = JpegDecoder.Decode(jpeg);

        decoded.Width.Should().Be(32);
        decoded.Height.Should().Be(32);
        (byte b, byte g, byte r, _) = decoded.Pixels.GetPixelBgra(16, 16);
        ((int)r).Should().BeCloseTo(200, 6);
        ((int)g).Should().BeCloseTo(100, 6);
        ((int)b).Should().BeCloseTo(50, 6);
    }

    [Fact]
    public void Gradient_RoundtripsWithinLossyTolerance()
    {
        ImageFrame original = GradientFrame(64, 64);
        byte[] jpeg = Encode(original, quality: 85);

        ImageFrame decoded = JpegDecoder.Decode(jpeg);

        double sumSquares = 0;
        int samples = 0;
        for (int y = 0; y < 64; y += 3)
        {
            for (int x = 0; x < 64; x += 3)
            {
                (byte ob, byte og, byte or, _) = original.Pixels.GetPixelBgra(x, y);
                (byte db, byte dg, byte dr, _) = decoded.Pixels.GetPixelBgra(x, y);
                sumSquares += ((or - dr) * (or - dr))
                            + ((og - dg) * (og - dg))
                            + ((ob - db) * (ob - db));
                samples += 3;
            }
        }

        double rms = Math.Sqrt(sumSquares / samples);
        rms.Should().BeLessThan(8.0, "quality 85 should reproduce a smooth gradient closely");
    }

    [Fact]
    public void Grayscale_EncodesSingleComponentAndRoundtrips()
    {
        ImageFrame original = ImageFrame.Create(24, 24, ImageColorFormat.Gray8);
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                byte v = (byte)(x * 10);
                original.Pixels.SetPixelBgra(x, y, v, v, v, 255);
            }
        }

        byte[] jpeg = Encode(original, quality: 90);

        // FindMarker returns the offset of the segment length bytes; the
        // payload starts 2 bytes later: precision, heightHi/Lo, widthHi/Lo,
        // componentCount.
        int sof = FindMarker(jpeg, 0xC0);
        sof.Should().BeGreaterThan(0);
        jpeg[sof + 7].Should().Be(1, "grayscale encodes one component");

        ImageFrame decoded = JpegDecoder.Decode(jpeg);
        (byte b, byte g, byte r, _) = decoded.Pixels.GetPixelBgra(12, 5);
        ((int)g).Should().BeCloseTo(120, 6);
        r.Should().Be(g);
        b.Should().Be(g);
    }

    [Fact]
    public void OddDimensions_EncodeWithEdgeReplication()
    {
        ImageFrame original = SolidFrame(13, 7, 10, 200, 30);
        byte[] jpeg = Encode(original, quality: 90);

        ImageFrame decoded = JpegDecoder.Decode(jpeg);

        decoded.Width.Should().Be(13);
        decoded.Height.Should().Be(7);
        (byte b, byte g, byte r, _) = decoded.Pixels.GetPixelBgra(12, 6);
        ((int)g).Should().BeCloseTo(200, 8);
        ((int)r).Should().BeCloseTo(10, 8);
        ((int)b).Should().BeCloseTo(30, 8);
    }

    [Fact]
    public void Markers_CarrySoiSofDimensionsAndEoi()
    {
        byte[] jpeg = Encode(SolidFrame(300, 200, 1, 2, 3));

        jpeg[0].Should().Be(0xFF);
        jpeg[1].Should().Be(0xD8);
        jpeg[^2].Should().Be(0xFF);
        jpeg[^1].Should().Be(0xD9);

        int sof = FindMarker(jpeg, 0xC0);
        sof.Should().BeGreaterThan(0);
        int height = (jpeg[sof + 3] << 8) | jpeg[sof + 4];
        int width = (jpeg[sof + 5] << 8) | jpeg[sof + 6];
        height.Should().Be(200);
        width.Should().Be(300);
    }

    [Fact]
    public void Quality_ControlsOutputSize()
    {
        ImageFrame frame = GradientFrame(96, 96);
        byte[] high = Encode(frame, quality: 95);
        byte[] low = Encode(frame, quality: 20);

        high.Length.Should().BeGreaterThan(low.Length);
    }

    [Fact]
    public void Cmyk_Throws()
    {
        ImageFrame frame = ImageFrame.Create(8, 8, ImageColorFormat.Cmyk32);
        using MemoryStream ms = new();
        Action act = () => JpegEncoder.Encode(frame, ms);
        act.Should().Throw<ImageException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Quality_OutOfRange_Throws(int quality)
    {
        ImageFrame frame = SolidFrame(8, 8, 0, 0, 0);
        using MemoryStream ms = new();
        Action act = () => JpegEncoder.Encode(frame, ms, quality);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Scans for an 0xFF marker outside entropy data (header section only:
    // markers before SOS are unambiguous). Returns the payload offset
    // (after the 2-byte length), or -1.
    private static int FindMarker(byte[] jpeg, byte marker)
    {
        int i = 2;
        while (i + 3 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                return -1;
            }
            byte m = jpeg[i + 1];
            if (m == marker)
            {
                return i + 2;
            }
            if (m == 0xDA)
            {
                return -1;                            // reached SOS
            }
            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            i += 2 + length;
        }
        return -1;
    }
}
