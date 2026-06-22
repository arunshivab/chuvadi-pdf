// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2); PDF 32000-1:2008 §7.4.7.
// PHASE: Phase 2 — items 22/23.
//
// Conformance against a real JBIG2 stream produced by an independent encoder: the
// public "Hey Norconex, this is a test." sample (symbol dictionary in the globals
// stream, text region in the image stream). The expected bitmap was produced by
// this decoder and visually verified to read the correct text; the fixture then
// guards against regression across the whole stack (MQ coder, generic region,
// integer coders, symbol dictionary, text region).

using System;
using System.IO;
using Chuvadi.Pdf.Filters.Jbig2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class Jbig2ConformanceTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Jbig2");

    [Fact]
    public void Decode_NorconexSample_MatchesReferenceBitmap()
    {
        byte[] globals = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex_globals.bin"));
        byte[] image = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex_image.bin"));
        byte[] expected = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex.expected"));

        Jbig2Bitmap page = new Jbig2Decoder().Decode(image, globals);

        int expectedWidth = (expected[0] << 8) | expected[1];
        page.Width.Should().Be(expectedWidth);

        int rowBytes = (page.Width + 7) / 8;
        page.Height.Should().Be((expected.Length - 2) / rowBytes);

        byte[] packed = Pack(page, rowBytes);
        byte[] expectedBody = new byte[expected.Length - 2];
        Array.Copy(expected, 2, expectedBody, 0, expectedBody.Length);

        packed.Should().Equal(expectedBody);
    }

    private static byte[] Pack(Jbig2Bitmap page, int rowBytes)
    {
        byte[] packed = new byte[rowBytes * page.Height];
        int offset = 0;
        for (int y = 0; y < page.Height; y++)
        {
            for (int x = 0; x < page.Width; x++)
            {
                if (page.Get(x, y) != 0)
                {
                    packed[offset + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }

            offset += rowBytes;
        }

        return packed;
    }
}
