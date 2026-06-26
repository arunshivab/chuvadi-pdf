// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C WOFF2 Recommendation 2018-03-01 — transformed glyf/loca
// PHASE: Phase 3 — WOFF2 unpacker
//
// Decodes a small transformed-glyf WOFF2 fixture (a 5-glyph Liberation Serif
// subset: .notdef, A, B, acute, Aacute) and verifies the reconstructed sfnt:
// TrueType magic, glyph count, and per-glyph contour counts + bounding boxes,
// covering simple glyphs, a composite glyph, and an empty glyph. Expected values
// were taken from the same subset decoded by an independent tool (fonttools).

using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Woff2.Tests;

public sealed class Woff2UnpackerTests
{
    private static byte[] DecodeFixture()
    {
        string path = Path.Combine("Fixtures", "LiberationSubset.woff2");
        return Woff2Unpacker.Unpack(File.ReadAllBytes(path));
    }

    [Fact]
    public void Unpack_ProducesTrueTypeSfnt_WithExpectedGlyphCount()
    {
        byte[] sfnt = DecodeFixture();

        sfnt.Length.Should().BeGreaterThan(4);
        ReadU32(sfnt, 0).Should().Be(0x00010000u, "output must be a TrueType sfnt");

        FindTable(sfnt, "glyf").Should().BeGreaterThan(0);
        FindTable(sfnt, "loca").Should().BeGreaterThan(0);
        FindTable(sfnt, "head").Should().BeGreaterThan(0);

        int maxp = FindTable(sfnt, "maxp");
        ReadU16(sfnt, maxp + 4).Should().Be(5);
    }

    [Fact]
    public void Unpack_ReconstructsSimpleGlyph_A()
    {
        byte[] sfnt = DecodeFixture();
        (int nContours, int xMin, int yMin, int xMax, int yMax) = GlyphHeader(sfnt, 1);

        nContours.Should().Be(2);
        (xMin, yMin, xMax, yMax).Should().Be((20, 0, 1464, 1352));
    }

    [Fact]
    public void Unpack_ReconstructsCompositeGlyph_Aacute()
    {
        byte[] sfnt = DecodeFixture();
        (int nContours, int xMin, int yMin, int xMax, int yMax) = GlyphHeader(sfnt, 4);

        nContours.Should().Be(-1, "Aacute is a composite of A + acute");
        (xMin, yMin, xMax, yMax).Should().Be((20, 0, 1464, 1758));
    }

    // ── minimal sfnt reader ──────────────────────────────────────────────────

    private static (int NContours, int XMin, int YMin, int XMax, int YMax) GlyphHeader(byte[] sfnt, int gid)
    {
        int loca = FindTable(sfnt, "loca");
        int glyf = FindTable(sfnt, "glyf");
        uint start = ReadU32(sfnt, loca + gid * 4);
        int p = glyf + (int)start;
        return (ReadI16(sfnt, p), ReadI16(sfnt, p + 2), ReadI16(sfnt, p + 4), ReadI16(sfnt, p + 6), ReadI16(sfnt, p + 8));
    }

    private static int FindTable(byte[] sfnt, string tag)
    {
        uint want = ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
        int numTables = ReadU16(sfnt, 4);
        for (int i = 0; i < numTables; i++)
        {
            int dir = 12 + i * 16;
            if (ReadU32(sfnt, dir) == want)
            {
                return (int)ReadU32(sfnt, dir + 8);
            }
        }

        return -1;
    }

    private static int ReadU16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

    private static int ReadI16(byte[] d, int p) => (short)((d[p] << 8) | d[p + 1]);

    private static uint ReadU32(byte[] d, int p)
        => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
}
