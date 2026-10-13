// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType glyf — composite glyphs; FreeType autohinter policy
// PHASE: 3.18 — composite-glyph Y-fitting in unhinted fonts.
//
// Before 3.18, a composite glyph in a font with no hinting machinery was
// assembled through the (no-op) bytecode path and came back un-grid-fitted,
// while its simple base glyph WAS autohinted — accented letters rendered with
// different Y extremes than their bases. The composite is now flattened and
// run through the same geometric autohinter.

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class CompositeAutohintTests
{
    private const int UnitsPerEm = 1000;
    private const int BoxGlyphId = 1;
    private const int CompositeGlyphId = 2;

    [Fact]
    public void UnhintedFont_CompositeGlyph_IsAutohinted()
    {
        TrueTypeLoader loader = new(BuildTtf());

        GlyphOutline? composite = loader.GetHintedGlyphOutline(
            CompositeGlyphId, ppem: 50, light: true, autohintFallback: true);

        composite.Should().NotBeNull(
            "a composite in an unhinted font flattens and autohints like a simple glyph");
    }

    [Fact]
    public void UnhintedFont_CompositeAndBase_ShareTheSameFittedYExtremes()
    {
        TrueTypeLoader loader = new(BuildTtf());

        GlyphOutline? baseGlyph = loader.GetHintedGlyphOutline(
            BoxGlyphId, ppem: 50, light: true, autohintFallback: true);
        GlyphOutline? composite = loader.GetHintedGlyphOutline(
            CompositeGlyphId, ppem: 50, light: true, autohintFallback: true);

        baseGlyph.Should().NotBeNull();
        composite.Should().NotBeNull();

        // The composite places the box with dx=50, dy=0: identical Y geometry,
        // so both must grid-fit to the same Y extremes.
        (double BaseMin, double BaseMax) = YExtremes(baseGlyph!.Outline);
        (double CompMin, double CompMax) = YExtremes(composite!.Outline);

        CompMin.Should().BeApproximately(BaseMin, 1e-6,
            "an x-only component offset must not change the fitted Y bottom");
        CompMax.Should().BeApproximately(BaseMax, 1e-6,
            "an x-only component offset must not change the fitted Y top");
    }

    [Fact]
    public void UnhintedFont_CompositeGlyph_AutohintOff_IsNotGridFitted()
    {
        // With the autohinter opted out, the composite keeps the pre-3.18
        // path: assembled through the (no-op) bytecode machinery, so its Y
        // extremes stay naturally scaled (707 × 0.05 = 35.35) instead of being
        // snapped to the pixel grid.
        TrueTypeLoader loader = new(BuildTtf());

        GlyphOutline? fitted = loader.GetHintedGlyphOutline(
            CompositeGlyphId, ppem: 50, light: true, autohintFallback: true);
        GlyphOutline? unfitted = loader.GetHintedGlyphOutline(
            CompositeGlyphId, ppem: 50, light: true, autohintFallback: false);

        fitted.Should().NotBeNull();
        double fittedTop = YExtremes(fitted!.Outline).Max;

        fittedTop.Should().BeApproximately(System.Math.Round(fittedTop), 1e-6,
            "the autohinted top snaps to the pixel grid");

        if (unfitted is not null)
        {
            YExtremes(unfitted.Outline).Max.Should().NotBe(fittedTop,
                "the opt-out must not produce the grid-fitted outline");
        }
    }

    private static (double Min, double Max) YExtremes(Path outline)
    {
        RectangleF bounds = outline.EndpointBounds();
        return (bounds.Y, bounds.Y + bounds.Height);
    }

    // ── Synthetic two-glyph TTF: glyph 1 = simple box, glyph 2 = composite
    //    referencing glyph 1 at dx=50, dy=0. No fpgm/prep → unhinted font. ──

    private static byte[] BuildTtf()
    {
        byte[] g1 = PadEven(BuildBoxGlyph());
        byte[] g2 = PadEven(BuildCompositeGlyph());
        int[] locaBytes = [0, 0, g1.Length, g1.Length + g2.Length];

        byte[] head = BuildHead();
        byte[] hhea = BuildHhea();
        byte[] maxp = BuildMaxp();
        byte[] hmtx = BuildHmtx();
        byte[] cmap = [0x00, 0x00, 0x00, 0x00];
        byte[] cvt = [0x00, 0x00];

        List<byte> glyf = new();
        glyf.AddRange(g1);
        glyf.AddRange(g2);

        List<byte> loca = new();
        foreach (int off in locaBytes)
        {
            loca.AddRange(U16(off / 2));
        }

        (string Tag, byte[] Data)[] tables =
        [
            ("cmap", cmap),
            ("cvt ", cvt),
            ("glyf", glyf.ToArray()),
            ("head", head),
            ("hhea", hhea),
            ("hmtx", hmtx),
            ("loca", loca.ToArray()),
            ("maxp", maxp),
        ];

        int n = tables.Length;
        int entrySelector = 0;
        int power = 1;
        while (power * 2 <= n)
        {
            power *= 2;
            entrySelector++;
        }

        int searchRange = power * 16;
        int rangeShift = (n * 16) - searchRange;

        List<byte> data = new();
        data.AddRange([0x00, 0x01, 0x00, 0x00]);
        data.AddRange(U16(n));
        data.AddRange(U16(searchRange));
        data.AddRange(U16(entrySelector));
        data.AddRange(U16(rangeShift));

        int dataStart = data.Count + (n * 16);
        int[] offsets = new int[n];
        int cursor = dataStart;
        for (int i = 0; i < n; i++)
        {
            offsets[i] = cursor;
            int padded = tables[i].Data.Length + ((4 - (tables[i].Data.Length % 4)) % 4);
            cursor += padded;
        }

        for (int i = 0; i < n; i++)
        {
            foreach (char c in tables[i].Tag)
            {
                data.Add((byte)c);
            }

            data.AddRange(U32(0));
            data.AddRange(U32((uint)offsets[i]));
            data.AddRange(U32((uint)tables[i].Data.Length));
        }

        for (int i = 0; i < n; i++)
        {
            data.AddRange(tables[i].Data);
            int pad = (4 - (tables[i].Data.Length % 4)) % 4;
            for (int j = 0; j < pad; j++)
            {
                data.Add(0);
            }
        }

        return data.ToArray();
    }

    private static byte[] BuildBoxGlyph()
    {
        List<byte> g = new();
        g.AddRange(S16(1));                          // numberOfContours
        g.AddRange(S16(100));                        // xMin
        g.AddRange(S16(0));                          // yMin
        g.AddRange(S16(300));                        // xMax
        g.AddRange(S16(707));                        // yMax
        g.AddRange(U16(3));                          // endPtsOfContours[0]
        g.AddRange(U16(0));                          // instructionLength

        for (int i = 0; i < 4; i++)
        {
            g.Add(0x01);                             // ON_CURVE, long coords
        }

        int[] xs = [100, 300, 300, 100];
        int[] ys = [0, 0, 707, 707];

        int prev = 0;
        foreach (int x in xs)
        {
            g.AddRange(S16(x - prev));
            prev = x;
        }

        prev = 0;
        foreach (int y in ys)
        {
            g.AddRange(S16(y - prev));
            prev = y;
        }

        return g.ToArray();
    }

    private static byte[] BuildCompositeGlyph()
    {
        List<byte> g = new();
        g.AddRange(S16(-1));                         // composite
        g.AddRange(S16(150));                        // xMin
        g.AddRange(S16(0));                          // yMin
        g.AddRange(S16(350));                        // xMax
        g.AddRange(S16(707));                        // yMax
        g.AddRange(U16(0x0003));                     // ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES
        g.AddRange(U16(BoxGlyphId));                 // component glyph
        g.AddRange(S16(50));                         // dx
        g.AddRange(S16(0));                          // dy
        return g.ToArray();
    }

    private static byte[] BuildHead()
    {
        List<byte> h = new();
        h.AddRange(U32(0x00010000));
        h.AddRange(U32(0));
        h.AddRange(U32(0));
        h.AddRange(U32(0x5F0F3CF5));
        h.AddRange(U16(0));
        h.AddRange(U16(UnitsPerEm));
        h.AddRange(new byte[16]);
        h.AddRange(S16(0));
        h.AddRange(S16(0));
        h.AddRange(S16(350));
        h.AddRange(S16(707));
        h.AddRange(U16(0));
        h.AddRange(U16(8));
        h.AddRange(U16(2));
        h.AddRange(U16(0));                          // short loca
        h.AddRange(U16(0));
        return h.ToArray();
    }

    private static byte[] BuildHhea()
    {
        List<byte> h = new();
        h.AddRange(U32(0x00010000));
        h.AddRange(new byte[30]);
        h.AddRange(U16(3));                          // numberOfHMetrics
        return h.ToArray();
    }

    private static byte[] BuildMaxp()
    {
        List<byte> m = new();
        m.AddRange(U32(0x00010000));
        m.AddRange(U16(3));                          // numGlyphs
        m.AddRange(U16(4));
        m.AddRange(U16(1));
        m.AddRange(U16(1));
        m.AddRange(U16(1));
        m.AddRange(U16(2));
        m.AddRange(U16(0));
        m.AddRange(U16(16));
        m.AddRange(U16(16));
        m.AddRange(U16(0));
        m.AddRange(U16(64));
        m.AddRange(U16(16));
        m.AddRange(U16(2));
        m.AddRange(U16(0));
        return m.ToArray();
    }

    private static byte[] BuildHmtx()
    {
        List<byte> h = new();
        h.AddRange(U16(500));
        h.AddRange(S16(0));                          // GID0
        h.AddRange(U16(400));
        h.AddRange(S16(100));                        // GID1
        h.AddRange(U16(450));
        h.AddRange(S16(150));                        // GID2
        return h.ToArray();
    }

    private static byte[] PadEven(byte[] data)
    {
        if (data.Length % 2 == 0)
        {
            return data;
        }

        byte[] padded = new byte[data.Length + 1];
        System.Array.Copy(data, padded, data.Length);
        return padded;
    }

    private static byte[] U16(int v) => [(byte)(v >> 8), (byte)(v & 0xFF)];

    private static byte[] S16(int v) => U16(v & 0xFFFF);

    private static byte[] U32(uint v)
        => [(byte)(v >> 24), (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
}
