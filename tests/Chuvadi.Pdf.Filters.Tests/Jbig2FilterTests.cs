// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.7; ITU-T T.88 §7.
// PHASE: Phase 2 — item 22.
//
// End-to-end exercise of the segment parser, generic-region decode, page assembly,
// and 1-bpp packing. The embedded JBIG2 stream is constructed here (one generic
// region segment) and decoded back; bit-exact conformance against a stream from an
// independent JBIG2 encoder is checked separately against a reference fixture.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Filters.Jbig2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class Jbig2FilterTests
{
    [Fact]
    public void FilterName_IsJbig2Decode()
    {
        new Jbig2Filter().FilterName.Should().Be("JBIG2Decode");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Decode_SingleGenericRegion_RoundTrips(int template, bool tpgdon)
    {
        Jbig2Bitmap source = PatternBitmap(32, 24, 13 + template);
        byte[] stream = BuildGenericRegionStream(source, template, tpgdon);

        Jbig2Bitmap page = new Jbig2Decoder().Decode(stream, null);

        page.Width.Should().Be(source.Width);
        page.Height.Should().Be(source.Height);
        page.Data.Should().Equal(source.Data);
    }

    [Fact]
    public void Decode_ThroughFilter_PacksAndInvertsToPdfPolarity()
    {
        Jbig2Bitmap source = PatternBitmap(20, 16, 99);
        byte[] stream = BuildGenericRegionStream(source, 0, tpgdon: false);

        byte[] packed;
        using (MemoryStream input = new MemoryStream(stream))
        using (MemoryStream output = new MemoryStream())
        {
            new Jbig2Filter().Decode(input, output);
            packed = output.ToArray();
        }

        int rowBytes = (source.Width + 7) / 8;
        packed.Length.Should().Be(rowBytes * source.Height);

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int bit = (packed[(y * rowBytes) + (x >> 3)] >> (7 - (x & 7))) & 1;
                // Filter inverts: a JBIG2 black pixel (1) becomes sample bit 0.
                int pixel = 1 - bit;
                pixel.Should().Be(source.Get(x, y), "pixel ({0},{1})", x, y);
            }
        }
    }

    [Fact]
    public void Decode_MmrGenericRegion_IsRejectedClearly()
    {
        Jbig2Bitmap source = PatternBitmap(16, 16, 1);
        byte[] stream = BuildGenericRegionStream(source, 0, tpgdon: false, forceMmr: true);

        Action act = () => new Jbig2Decoder().Decode(stream, null);
        act.Should().Throw<FilterException>().WithMessage("*MMR*");
    }

    private static byte[] BuildGenericRegionStream(
        Jbig2Bitmap source, int template, bool tpgdon, bool forceMmr = false)
    {
        TemplatePixel[] at = GenericRegion.DefaultAt(template);
        byte[] coded = GenericRegion.Encode(source, template, at, tpgdon);

        List<byte> regionData = new List<byte>();
        WriteUInt32(regionData, (uint)source.Width);   // region width
        WriteUInt32(regionData, (uint)source.Height);  // region height
        WriteUInt32(regionData, 0);                    // region X
        WriteUInt32(regionData, 0);                    // region Y
        regionData.Add(0x00);                          // region flags: combine OR

        int genericFlags = (forceMmr ? 0x01 : 0x00) | (template << 1) | (tpgdon ? 0x08 : 0x00);
        regionData.Add((byte)genericFlags);

        foreach (TemplatePixel pixel in at)
        {
            regionData.Add(unchecked((byte)(sbyte)pixel.Dx));
            regionData.Add(unchecked((byte)(sbyte)pixel.Dy));
        }

        regionData.AddRange(coded);

        List<byte> stream = new List<byte>();
        WriteUInt32(stream, 0);                          // segment number
        stream.Add(0x26);                                // flags: type 38, 1-byte page assoc
        stream.Add(0x00);                                // referred-to count/retain
        stream.Add(0x01);                                // page association
        WriteUInt32(stream, (uint)regionData.Count);     // data length
        stream.AddRange(regionData);

        return stream.ToArray();
    }

    private static void WriteUInt32(List<byte> output, uint value)
    {
        output.Add((byte)(value >> 24));
        output.Add((byte)(value >> 16));
        output.Add((byte)(value >> 8));
        output.Add((byte)value);
    }

    private static Jbig2Bitmap PatternBitmap(int width, int height, int seed)
    {
        Random rng = new Random(seed);
        Jbig2Bitmap bitmap = new Jbig2Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.Set(x, y, rng.NextDouble() < 0.28 ? 1 : 0);
            }
        }

        return bitmap;
    }
}
