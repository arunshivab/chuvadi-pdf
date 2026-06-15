// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v3.x — image-export hardening (Windows WIC compatibility)

using System;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

/// <summary>
/// Regression tests for the TIFF encoder's Windows-WIC compatibility. The
/// encoder previously packed PackBits across the whole image in a single strip;
/// strict readers (Windows Photos, Photo Viewer, Paint — all WIC) reset the
/// algorithm at each row boundary, which shifted the rows and left a black band
/// at the bottom. The fix packs PackBits per scanline, splits the image into
/// multiple strips, and writes explicit Orientation and PlanarConfiguration
/// tags. These tests lock that structure in.
/// </summary>
public sealed class TiffWicCompatibilityTests
{
    [Fact]
    public void Encode_WideImage_IsMultiStripWithPerRowPackBitsAndOrientationTags()
    {
        // A wide image forces several ~8 KB strips so the multi-strip path runs.
        int width = 512;
        int height = 32;
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        byte[] tiff = TiffEncoder.Encode(frame);

        TiffTag compression = ReadTag(tiff, 259);
        TiffTag orientation = ReadTag(tiff, 274);
        TiffTag planar = ReadTag(tiff, 284);
        TiffTag stripOffsets = ReadTag(tiff, 273);
        TiffTag stripByteCounts = ReadTag(tiff, 279);
        TiffTag rowsPerStrip = ReadTag(tiff, 278);

        compression.Value.Should().Be(32773u, "PackBits compression");
        orientation.Value.Should().Be(1u, "top-left orientation");
        planar.Value.Should().Be(1u, "chunky (interleaved) pixels");
        stripOffsets.Count.Should().BeGreaterThan(1u, "image is split into multiple strips");
        stripByteCounts.Count.Should().Be(stripOffsets.Count);
        rowsPerStrip.Value.Should().BeLessThan((uint)height, "rows are grouped into strips");
    }

    [Fact]
    public void Encode_ThenDecode_PreservesDimensionsAcrossStripBoundaries()
    {
        // Distinct per-row content: a whole-image PackBits / row-shift regression
        // would corrupt the decode rather than round-trip cleanly.
        int width = 6;
        int height = 40;
        PixelBuffer buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                buffer.SetPixelBgra(x, y, b: (byte)(y * 6), g: (byte)(x * 40), r: (byte)(x + y), a: 255);
            }
        }
        ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);

        byte[] tiff = TiffEncoder.Encode(frame);
        System.Collections.Generic.List<ImageFrame> decoded = TiffDecoder.Decode(tiff);

        decoded.Should().HaveCount(1);
        decoded[0].Width.Should().Be(width);
        decoded[0].Height.Should().Be(height);
    }

    private readonly struct TiffTag
    {
        public TiffTag(ushort type, uint count, uint value)
        {
            Type = type;
            Count = count;
            Value = value;
        }

        public ushort Type { get; }

        public uint Count { get; }

        public uint Value { get; }
    }

    private static TiffTag ReadTag(byte[] tiff, ushort tag)
    {
        uint ifdOffset = ReadU32(tiff, 4);
        ushort entryCount = ReadU16(tiff, (int)ifdOffset);
        for (int i = 0; i < entryCount; i++)
        {
            int entry = (int)ifdOffset + 2 + (i * 12);
            if (ReadU16(tiff, entry) == tag)
            {
                return new TiffTag(ReadU16(tiff, entry + 2), ReadU32(tiff, entry + 4), ReadU32(tiff, entry + 8));
            }
        }

        throw new InvalidOperationException($"TIFF tag {tag} not present.");
    }

    private static ushort ReadU16(byte[] b, int offset)
        => (ushort)(b[offset] | (b[offset + 1] << 8));

    private static uint ReadU32(byte[] b, int offset)
        => (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
}
