// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Regression tests for LA-29: PageRasterizer dropped or garbled CFF (Type1C /
// FontFile3) text. Three defects are guarded here:
//   1. FontRenderer threw on bare CFF programs (only TrueType was wired up), so
//      the rasterizer got a null renderer and emitted no glyphs. FontRenderer
//      must now detect the CFF program, expose its glyph count and metrics, and
//      resolve a Unicode code point to a glyph index via the charset.
//   2. Type2Interpreter ignored the flex operators (the only one exercised by
//      the sample document was flex1, 12 37), dropping curve segments and
//      failing to advance the current point.
//   3. Type2Interpreter never closed contours, so a glyph's multiple contours
//      merged into one filled region with spurious connecting edges. Each
//      contour must now carry a ClosePath, matching the TrueType outline path.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class CffRasterizationRegressionTests
{
    // Type1C CFF: glyph order [.notdef, A, checkmark, ffi], 1000 upm, not
    // CID-keyed (same fixture as CffLoaderCharsetTests).
    private const string NameCffBase64 =
        "AQAEAQABAQEJTmFtZVRlc3QAAQEBD4uL+R75UAW/D4v2EsYRAAEBAQpjaGVja21hcmsAAAAAIgGHAQsABAEBCxUfKfiIixb4iPlQBg74uosW+Lr5UAYO+OyLFvjs+VAGDvkeixb5HvlQBg4=";

    private static readonly byte[] NameCff = Convert.FromBase64String(NameCffBase64);

    private static readonly List<byte[]> NoSubrs = new List<byte[]>();

    [Fact]
    public void FontRenderer_AcceptsBareCffProgram_WithoutThrowing()
    {
        Func<FontRenderer> act = () => new FontRenderer(NameCff);

        FontRenderer renderer = act.Should().NotThrow().Subject;
        renderer.NumGlyphs.Should().Be(4);
        renderer.UnitsPerEm.Should().Be(1000);
    }

    [Fact]
    public void FontRenderer_ResolvesCffGlyphIndexFromUnicode()
    {
        FontRenderer renderer = new FontRenderer(NameCff);

        // 'A' (U+0041) is glyph 1 in this font's charset.
        renderer.GetGlyphIndexUnicode('A').Should().Be(1);
    }

    [Fact]
    public void FontRenderer_ReturnsClosedNonEmptyOutlineForCffGlyph()
    {
        FontRenderer renderer = new FontRenderer(NameCff);

        GlyphOutline outline = renderer.GetGlyphOutline(1);

        outline.Outline.Segments.Should().NotBeEmpty();
        CountClosePaths(outline.Outline).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Type2Interpreter_NewContour_ClosesThePreviousContour()
    {
        // Two contours: rmoveto, rlineto, rmoveto, rlineto, endchar. The second
        // rmoveto must close the first contour; endchar must close the second.
        List<byte> cs = new List<byte>();
        AppendInt(cs, 100);
        AppendInt(cs, 100);
        cs.Add(21);             // rmoveto
        AppendInt(cs, 50);
        AppendInt(cs, 0);
        cs.Add(5);              // rlineto
        AppendInt(cs, 0);
        AppendInt(cs, 100);
        cs.Add(21);             // rmoveto (closes contour 1)
        AppendInt(cs, 50);
        AppendInt(cs, 0);
        cs.Add(5);              // rlineto
        cs.Add(14);             // endchar (closes contour 2)

        Type2Interpreter interpreter = new Type2Interpreter(NoSubrs, NoSubrs, 0, 0);
        Path path = interpreter.Run(cs.ToArray());

        CountClosePaths(path).Should().Be(2);
    }

    [Fact]
    public void Type2Interpreter_Flex1_EmitsTwoCurvesAndClosesContour()
    {
        // rmoveto, then flex1 (11 operands, 12 37), then endchar.
        List<byte> cs = new List<byte>();
        AppendInt(cs, 100);
        AppendInt(cs, 100);
        cs.Add(21);             // rmoveto
        for (int n = 0; n < 11; n++)
        {
            AppendInt(cs, 20);
        }

        cs.Add(12);
        cs.Add(37);             // flex1
        cs.Add(14);             // endchar

        Type2Interpreter interpreter = new Type2Interpreter(NoSubrs, NoSubrs, 0, 0);
        Path path = interpreter.Run(cs.ToArray());

        CountSegments(path, PathSegmentKind.CubicBezierTo).Should().Be(2);
        CountClosePaths(path).Should().Be(1);
    }

    private static int CountClosePaths(Path path)
    {
        return CountSegments(path, PathSegmentKind.ClosePath);
    }

    private static int CountSegments(Path path, PathSegmentKind kind)
    {
        int count = 0;
        foreach (PathSegment seg in path.Segments)
        {
            if (seg.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    // Encodes a Type 2 integer operand in the [-107, 107] single-byte range.
    private static void AppendInt(List<byte> cs, int value)
    {
        if (value < -107 || value > 107)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        cs.Add((byte)(value + 139));
    }
}
