// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — fallback policy for fonts with no bytecode
// PHASE: Phase 2.7 — Autohinting fallback wiring tests

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class AutohintFallbackTests
{
    private const int UnitsPerEm = 1000;
    private const int BoxGlyphId = 1;

    [Fact]
    public void UnhintedFont_AutohintOn_ReturnsGridFittedOutline()
    {
        TrueTypeLoader loader = new(BuildTtf(withPrepProgram: false));

        GlyphOutline? outline = loader.GetHintedGlyphOutline(
            BoxGlyphId, ppem: 50, light: true, autohintFallback: true);

        outline.Should().NotBeNull("an instruction-less glyph in an unhinted font autohints");
        outline!.Metrics.AdvanceWidth.Should().Be(20, "advance is hmtx 400 × scale 0.05, rounded");
    }

    [Fact]
    public void UnhintedFont_AutohintOff_FallsBackToNull()
    {
        TrueTypeLoader loader = new(BuildTtf(withPrepProgram: false));

        GlyphOutline? outline = loader.GetHintedGlyphOutline(
            BoxGlyphId, ppem: 50, light: true, autohintFallback: false);

        outline.Should().BeNull("the opt-out restores the unhinted fallback");
    }

    [Fact]
    public void HintedFont_InstructionlessGlyph_IsNotAutohinted()
    {
        // The font carries a prep program, so it counts as hinted — its
        // instruction-less glyphs keep falling back to the unhinted outline
        // for consistent weights with their bytecode-hinted neighbours.
        TrueTypeLoader loader = new(BuildTtf(withPrepProgram: true));

        GlyphOutline? outline = loader.GetHintedGlyphOutline(
            BoxGlyphId, ppem: 50, light: true, autohintFallback: true);

        outline.Should().BeNull();
    }

    [Fact]
    public void FontRenderer_CachesAutohintVariantsSeparately()
    {
        FontRenderer renderer = new(BuildTtf(withPrepProgram: false));

        GlyphOutline? on = renderer.GetHintedGlyphOutline(BoxGlyphId, 50, light: true, autohintFallback: true);
        GlyphOutline? off = renderer.GetHintedGlyphOutline(BoxGlyphId, 50, light: true, autohintFallback: false);
        GlyphOutline? onAgain = renderer.GetHintedGlyphOutline(BoxGlyphId, 50, light: true, autohintFallback: true);

        on.Should().NotBeNull();
        off.Should().BeNull("the cache key must distinguish the autohint flag");
        onAgain.Should().NotBeNull();
    }

    // ── Minimal single-glyph TTF assembler ────────────────────────────────

    // Builds a two-glyph TTF: GID0 empty, GID1 a simple instruction-less box
    // (100, 0)–(300, 707). When `withPrepProgram` is true a one-byte prep
    // program marks the font as carrying hinting machinery.
    private static byte[] BuildTtf(bool withPrepProgram)
    {
        byte[] box = BuildBoxGlyph();
        byte[] g1 = PadEven(box);
        int[] locaBytes = [0, 0, g1.Length];

        byte[] head = BuildHead();
        byte[] hhea = BuildHhea();
        byte[] maxp = BuildMaxp();
        byte[] hmtx = BuildHmtx();
        byte[] cmap = [0x00, 0x00, 0x00, 0x00];
        byte[] cvt = [0x00, 0x00];
        byte[] fpgm = [];
        byte[] prep = withPrepProgram ? new byte[] { 0x18 } : [];   // RTG

        List<byte> loca = new();
        foreach (int off in locaBytes)
        {
            loca.AddRange(U16(off / 2));
        }

        (string Tag, byte[] Data)[] tables =
        [
            ("cmap", cmap),
            ("cvt ", cvt),
            ("fpgm", fpgm),
            ("glyf", g1),
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
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(tables[i].Tag));
            data.AddRange(U32(0));
            data.AddRange(U32((uint)offsets[i]));
            data.AddRange(U32((uint)tables[i].Data.Length));
        }

        for (int i = 0; i < n; i++)
        {
            data.AddRange(tables[i].Data);
            int pad = (4 - (tables[i].Data.Length % 4)) % 4;
            for (int p = 0; p < pad; p++)
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
        g.AddRange(U16(0));                          // instructionLength = 0

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
        h.AddRange(S16(300));
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
        h.AddRange(new byte[30]);                    // ascender .. metricDataFormat
        h.AddRange(U16(2));                          // numberOfHMetrics (offset 34)
        return h.ToArray();
    }

    private static byte[] BuildMaxp()
    {
        List<byte> m = new();
        m.AddRange(U32(0x00010000));
        m.AddRange(U16(2));                          // numGlyphs
        m.AddRange(U16(4));
        m.AddRange(U16(1));
        m.AddRange(U16(0));
        m.AddRange(U16(0));
        m.AddRange(U16(2));
        m.AddRange(U16(0));
        m.AddRange(U16(16));
        m.AddRange(U16(16));
        m.AddRange(U16(0));
        m.AddRange(U16(64));
        m.AddRange(U16(16));
        m.AddRange(U16(0));
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
