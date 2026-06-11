// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType composite glyphs — hinted assembly (component offset, ROUND_XY_TO_GRID,
//        composite instruction stream) and the org<-cur program baseline.
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering tests (composite glyph hinting)

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

/// <summary>
/// Regression coverage for composite glyph hinting. A composite glyph (a glyph
/// assembled from component glyphs) is hinted by hinting each component, merging
/// the points at the component offset, and running the composite's own
/// instruction stream over the assembly. These tests build a synthetic font
/// in-memory (no external font file) with one simple base glyph and one
/// composite glyph that references it, so the hinted assembly path runs end to
/// end through the public loader API.
/// </summary>
public sealed class CompositeHintingTests
{
    // The composite's instruction stream:
    //   SVTCA[0]   (0x00) — set projection and freedom vectors to the Y axis
    //   PUSHB1 0   (0xB0 0x00)
    //   SRP2       (0x12) — rp2 := point 0
    //   PUSHB1 0   (0xB0 0x00)
    //   SHC[0]     (0x34) — shift contour 0 by rp2's (current - original) displacement
    //
    // SHC measures the reference point's displacement as current minus original.
    // The composite assembly leaves point 0 with current != original before the
    // program (the ROUND_XY_TO_GRID offset rounds the current coordinate but not
    // the original), so unless the interpreter rebases original to current at the
    // start of a composite program, SHC shifts the whole contour by that stale
    // gap. With the rebase (org <- cur), the displacement is zero and the contour
    // is unmoved. This program therefore discriminates the org<-cur behaviour.
    private static readonly byte[] DiscriminatorProgram =
    [
        0x00, 0xB0, 0x00, 0x12, 0xB0, 0x00, 0x34,
    ];

    private const int UnitsPerEm = 1000;
    private const int TestPpem = 100;        // scale = 0.1 device px per font unit
    private const int CompositeGlyphId = 2;
    private const int BaseGlyphId = 1;

    [Fact]
    public void Composite_Hinted_ReturnsNonEmptyOutline()
    {
        byte[] font = BuildCompositeTtf(scaledComponent: false);
        TrueTypeLoader loader = new TrueTypeLoader(font);

        GlyphOutline? hinted = loader.GetHintedGlyphOutline(CompositeGlyphId, TestPpem, light: true);

        hinted.Should().NotBeNull();
        hinted!.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Composite_GridRoundedOffset_PlacesComponentOnPixelGrid()
    {
        // dy = 234 font units -> 23.4 device px; ROUND_XY_TO_GRID rounds the
        // applied (current) offset to 23 px. The base box's bottom edge (design
        // y = 0) therefore lands at device y = 23 px, an integer pixel.
        byte[] font = BuildCompositeTtf(scaledComponent: false);
        TrueTypeLoader loader = new TrueTypeLoader(font);

        GlyphOutline outline = loader.GetHintedGlyphOutline(CompositeGlyphId, TestPpem, light: true)!;

        double bottom = MinY(outline.Outline);
        bottom.Should().BeApproximately(23.0, 0.05);
    }

    [Fact]
    public void Composite_InstructionStream_MeasuresFromAssembledPositions()
    {
        // The headline regression: with org<-cur, the SHC reference displacement
        // is zero, so the contour top stays at its assembled position
        //   base top (design y = 700 -> 70 px) + grid-rounded offset (23 px) = 93 px.
        // Without org<-cur, SHC would shift the contour by the stale current-minus-
        // original gap (-0.4 px), pulling the top to 92.6 px. Asserting 93.0 px
        // locks the org<-cur composite-program baseline.
        byte[] font = BuildCompositeTtf(scaledComponent: false);
        TrueTypeLoader loader = new TrueTypeLoader(font);

        GlyphOutline outline = loader.GetHintedGlyphOutline(CompositeGlyphId, TestPpem, light: true)!;

        double top = MaxY(outline.Outline);
        top.Should().BeApproximately(93.0, 0.05);
    }

    [Fact]
    public void Composite_ScaledComponent_DoesNotUseGridRoundedHintedPath()
    {
        // A component with WE_HAVE_A_SCALE is outside the hinted-composite scope.
        // The loader must not produce the hinted, grid-rounded assembly for it: it
        // either returns null (caller falls back to the scaled unhinted outline) or
        // produces an outline that is not the grid-rounded hinted result. Either
        // way, the bottom edge must differ from the hinted path's 23.0 px, proving
        // the scaled component did not go through grid-rounded composite hinting.
        byte[] scaledFont = BuildCompositeTtf(scaledComponent: true);
        TrueTypeLoader scaledLoader = new TrueTypeLoader(scaledFont);

        GlyphOutline? scaledHinted =
            scaledLoader.GetHintedGlyphOutline(CompositeGlyphId, TestPpem, light: true);

        if (scaledHinted is null)
        {
            // Bailed: the public renderer would use the scaled unhinted outline.
            // Assert that fallback is available and non-empty.
            GlyphOutline unhinted = scaledLoader.GetGlyphOutline(CompositeGlyphId);
            unhinted.IsEmpty.Should().BeFalse();
        }
        else
        {
            // Returned an outline, but it must not be the grid-rounded hinted
            // result (whose bottom sits exactly at 23.0 px).
            double bottom = MinY(scaledHinted.Outline);
            bottom.Should().NotBeApproximately(23.0, 0.05);
        }
    }

    [Fact]
    public void SimpleComponentGlyph_WithoutInstructions_ReturnsNullFromHintedPath()
    {
        // The base box carries no instruction stream. A simple glyph with no
        // program returns null from the hinted path (the caller then uses the
        // scaled unhinted outline). This is the precondition that makes composite
        // hinting non-trivial: the component is unhinted on its own, yet the
        // composite assembles and hints around it.
        byte[] font = BuildCompositeTtf(scaledComponent: false);
        TrueTypeLoader loader = new TrueTypeLoader(font);

        GlyphOutline? hinted = loader.GetHintedGlyphOutline(BaseGlyphId, TestPpem, light: true);

        hinted.Should().BeNull();
    }

    // ── Outline measurement helpers ────────────────────────────────────────

    private static double MaxY(Path path)
    {
        double max = double.MinValue;
        foreach (PathSegment seg in path.Segments)
        {
            if (seg.Kind == PathSegmentKind.ClosePath)
            {
                continue;
            }

            if (seg.P0.Y > max)
            {
                max = seg.P0.Y;
            }
        }

        return max;
    }

    private static double MinY(Path path)
    {
        double min = double.MaxValue;
        foreach (PathSegment seg in path.Segments)
        {
            if (seg.Kind == PathSegmentKind.ClosePath)
            {
                continue;
            }

            if (seg.P0.Y < min)
            {
                min = seg.P0.Y;
            }
        }

        return min;
    }

    // ── Synthetic composite font builder ───────────────────────────────────

    /// <summary>
    /// Builds a structurally valid TrueType font with three glyphs: .notdef
    /// (empty), a simple base box, and a composite that references the base at a
    /// grid-rounded Y offset and carries an instruction stream. When
    /// <paramref name="scaledComponent"/> is true the component additionally sets
    /// WE_HAVE_A_SCALE, taking it outside the hinted-composite scope.
    /// </summary>
    private static byte[] BuildCompositeTtf(bool scaledComponent)
    {
        byte[] baseGlyph = BuildBaseGlyph();
        byte[] composite = BuildCompositeGlyph(scaledComponent);

        // glyf: GID0 empty, GID1 base, GID2 composite. Each padded to even length
        // so the short loca format (offset / 2) stays integral.
        byte[] g1 = PadEven(baseGlyph);
        byte[] g2 = PadEven(composite);

        List<byte> glyf = new List<byte>();
        glyf.AddRange(g1);
        glyf.AddRange(g2);

        int[] locaBytes = [0, 0, g1.Length, g1.Length + g2.Length];

        return Assemble(glyf.ToArray(), locaBytes);
    }

    private static byte[] BuildBaseGlyph()
    {
        // Simple box: (100,0)(300,0)(300,700)(100,700), all on-curve, one contour.
        List<byte> g = new List<byte>();
        g.AddRange(S16(1));                          // numberOfContours
        g.AddRange(S16(100));                        // xMin
        g.AddRange(S16(0));                          // yMin
        g.AddRange(S16(300));                        // xMax
        g.AddRange(S16(700));                        // yMax
        g.AddRange(U16(3));                          // endPtsOfContours[0] = 3
        g.AddRange(U16(0));                          // instructionLength = 0

        for (int i = 0; i < 4; i++)
        {
            g.Add(0x01);                             // flags: ON_CURVE, long coords
        }

        int[] xs = [100, 300, 300, 100];
        int[] ys = [0, 0, 700, 700];

        int prevX = 0;
        foreach (int x in xs)
        {
            g.AddRange(S16(x - prevX));
            prevX = x;
        }

        int prevY = 0;
        foreach (int y in ys)
        {
            g.AddRange(S16(y - prevY));
            prevY = y;
        }

        return g.ToArray();
    }

    private static byte[] BuildCompositeGlyph(bool scaledComponent)
    {
        List<byte> g = new List<byte>();
        g.AddRange(S16(-1));                         // numberOfContours = -1 (composite)
        g.AddRange(S16(0));                          // xMin
        g.AddRange(S16(0));                          // yMin
        g.AddRange(S16(300));                        // xMax
        g.AddRange(S16(900));                        // yMax

        // Component flags: ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES |
        // ROUND_XY_TO_GRID | WE_HAVE_INSTRUCTIONS, plus WE_HAVE_A_SCALE when asked.
        int flags = 0x0001 | 0x0002 | 0x0004 | 0x0100;
        if (scaledComponent)
        {
            flags |= 0x0008;
        }

        g.AddRange(U16(flags));
        g.AddRange(U16(BaseGlyphId));                // glyphIndex of the component
        g.AddRange(S16(0));                          // dx (words)
        g.AddRange(S16(234));                        // dy (words) -> 23.4 px at ppem 100

        if (scaledComponent)
        {
            g.AddRange(S16(0x4000));                 // scale = 1.0 in F2Dot14
        }

        g.AddRange(U16(DiscriminatorProgram.Length));
        g.AddRange(DiscriminatorProgram);

        return g.ToArray();
    }

    private static byte[] Assemble(byte[] glyf, int[] locaByteOffsets)
    {
        byte[] head = BuildHead();
        byte[] hhea = BuildHhea();
        byte[] maxp = BuildMaxp();
        byte[] hmtx = BuildHmtx();
        byte[] cmap = [0x00, 0x00, 0x00, 0x00];      // version 0, numTables 0
        byte[] cvt = [0x00, 0x00, 0x02, 0xBC];       // cvt[0]=0, cvt[1]=700
        byte[] fpgm = [];
        byte[] prep = [];

        List<byte> loca = new List<byte>();
        foreach (int off in locaByteOffsets)
        {
            loca.AddRange(U16(off / 2));             // short loca: offset / 2
        }

        // Tables in alphabetical tag order, as required by the directory.
        (string Tag, byte[] Data)[] tables =
        [
            ("cmap", cmap),
            ("cvt ", cvt),
            ("fpgm", fpgm),
            ("glyf", glyf),
            ("head", head),
            ("hhea", hhea),
            ("hmtx", hmtx),
            ("loca", loca.ToArray()),
            ("maxp", maxp),
            ("prep", prep),
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
        int rangeShift = n * 16 - searchRange;

        List<byte> data = new List<byte>();
        data.AddRange([0x00, 0x01, 0x00, 0x00]);     // sfVersion
        data.AddRange(U16(n));
        data.AddRange(U16(searchRange));
        data.AddRange(U16(entrySelector));
        data.AddRange(U16(rangeShift));

        int dataStart = data.Count + n * 16;
        int[] offsets = new int[n];
        int cursor = dataStart;

        for (int i = 0; i < n; i++)
        {
            offsets[i] = cursor;
            int padded = tables[i].Data.Length + (4 - tables[i].Data.Length % 4) % 4;
            cursor += padded;
        }

        for (int i = 0; i < n; i++)
        {
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(tables[i].Tag));
            data.AddRange(U32(0));                   // checksum (not validated)
            data.AddRange(U32((uint)offsets[i]));
            data.AddRange(U32((uint)tables[i].Data.Length));
        }

        for (int i = 0; i < n; i++)
        {
            data.AddRange(tables[i].Data);
            int pad = (4 - tables[i].Data.Length % 4) % 4;

            for (int p = 0; p < pad; p++)
            {
                data.Add(0);
            }
        }

        return data.ToArray();
    }

    private static byte[] BuildHead()
    {
        List<byte> h = new List<byte>();
        h.AddRange(U32(0x00010000));                 // version
        h.AddRange(U32(0));                          // fontRevision
        h.AddRange(U32(0));                          // checkSumAdjustment
        h.AddRange(U32(0x5F0F3CF5));                 // magicNumber
        h.AddRange(U16(0));                          // flags
        h.AddRange(U16(UnitsPerEm));                 // unitsPerEm
        h.AddRange(new byte[16]);                    // created + modified
        h.AddRange(S16(0));                          // xMin
        h.AddRange(S16(0));                          // yMin
        h.AddRange(S16(300));                        // xMax
        h.AddRange(S16(900));                        // yMax
        h.AddRange(U16(0));                          // macStyle
        h.AddRange(U16(8));                          // lowestRecPPEM
        h.AddRange(U16(2));                          // fontDirectionHint
        h.AddRange(U16(0));                          // indexToLocFormat = 0 (short)
        h.AddRange(U16(0));                          // glyphDataFormat
        return h.ToArray();
    }

    private static byte[] BuildHhea()
    {
        List<byte> h = new List<byte>();
        h.AddRange(U32(0x00010000));                 // version
        h.AddRange(new byte[28]);                    // ascender..reserved
        h.AddRange(U16(3));                          // numberOfHMetrics = 3
        return h.ToArray();
    }

    private static byte[] BuildMaxp()
    {
        List<byte> m = new List<byte>();
        m.AddRange(U32(0x00010000));                 // version 1.0
        m.AddRange(U16(3));                          // numGlyphs
        m.AddRange(U16(4));                          // maxPoints
        m.AddRange(U16(1));                          // maxContours
        m.AddRange(U16(4));                          // maxCompositePoints
        m.AddRange(U16(1));                          // maxCompositeContours
        m.AddRange(U16(2));                          // maxZones
        m.AddRange(U16(0));                          // maxTwilightPoints
        m.AddRange(U16(16));                         // maxStorage
        m.AddRange(U16(16));                         // maxFunctionDefs
        m.AddRange(U16(0));                          // maxInstructionDefs
        m.AddRange(U16(64));                         // maxStackElements
        m.AddRange(U16(16));                         // maxSizeOfInstructions
        m.AddRange(U16(1));                          // maxComponentElements
        m.AddRange(U16(1));                          // maxComponentDepth
        return m.ToArray();
    }

    private static byte[] BuildHmtx()
    {
        List<byte> h = new List<byte>();
        h.AddRange(U16(500));
        h.AddRange(S16(0));                          // GID0 advance/lsb
        h.AddRange(U16(400));
        h.AddRange(S16(100));                        // GID1
        h.AddRange(U16(400));
        h.AddRange(S16(0));                          // GID2
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

    private static byte[] U16(int v)
    {
        return [(byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
    }

    private static byte[] S16(int v)
    {
        return [(byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
    }

    private static byte[] U32(uint v)
    {
        return
        [
            (byte)((v >> 24) & 0xFF),
            (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF),
            (byte)(v & 0xFF),
        ];
    }
}
