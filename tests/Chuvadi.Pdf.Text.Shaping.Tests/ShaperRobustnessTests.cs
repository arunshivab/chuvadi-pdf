// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.26: the shaper stays bounded and exception-free on hostile or broken
// layout tables, and keeps Indic reordering when a font has no usable GSUB.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Fonts.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class ShaperRobustnessTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void SelfRecursiveContextLookup_StopsWithinTheOperationBudget()
    {
        // A context lookup whose two records both call itself: exponential
        // without HarfBuzz's operation budget.
        byte[] carlito = File.ReadAllBytes(Path.Combine(Fixtures, "Carlito-Regular.ttf"));
        int gidA = FontFace.Load(carlito).GetGlyphIndex('a');
        byte[] font = WithGsub(carlito, RecursiveGsub(gidA));

        Stopwatch watch = Stopwatch.StartNew();
        IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(font, "aaaaaaaaaaaa", LipiScript.Latin);
        watch.Stop();

        glyphs.Should().NotBeEmpty();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void IndicFont_WithoutGsub_StillReordersThePreBaseMatra()
    {
        byte[] tamil = File.ReadAllBytes(Path.Combine(Fixtures, "LiPi-Sans-Tamil.ttf"));
        RenameTable(tamil, "GSUB", "XSUB");

        IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(tamil, "கெ", LipiScript.Tamil);

        glyphs.Select(g => g.GlyphId).Should().Equal(46, 18);
    }

    [Fact]
    public void CorruptLayoutTables_NeverThrow()
    {
        byte[] original = File.ReadAllBytes(Path.Combine(Fixtures, "LiPi-Sans-Tamil.ttf"));
        Random random = new Random(26);
        foreach (string tag in new[] { "GSUB", "GPOS", "GDEF" })
        {
            (int offset, int length) = FindTable(original, tag);
            for (int round = 0; round < 60; round++)
            {
                byte[] font = (byte[])original.Clone();
                for (int k = 0; k < 8; k++)
                {
                    font[offset + random.Next(Math.Min(length, 4096))] = (byte)random.Next(256);
                }

                Action shape = () => TextShaper.Shape(font, "கொடு தமிழ் abc", LipiScript.Tamil);
                shape.Should().NotThrow($"{tag} round {round}");
            }
        }
    }

    private static byte[] RecursiveGsub(int glyph)
    {
        List<byte> b = new List<byte>();
        void U16(int v)
        {
            b.Add((byte)(v >> 8));
            b.Add((byte)v);
        }

        void Tag(string t) => b.AddRange(t.Select(c => (byte)c));

        // Header: version 1.0, ScriptList at 10, FeatureList at 30, LookupList at 44.
        U16(1); U16(0); U16(10); U16(30); U16(44);

        // ScriptList (10): one script 'latn' -> Script at +8; LangSys default at +4: one feature (0).
        U16(1); Tag("latn"); U16(8);
        U16(4); U16(0);
        U16(0); U16(0xFFFF); U16(1); U16(0);

        // FeatureList (30): 'liga' -> Feature at +8 with lookup 0.
        U16(1); Tag("liga"); U16(8);
        U16(0); U16(1); U16(0);

        // LookupList (44): one lookup at +4: type 5, flag 0, one subtable at +8.
        U16(1); U16(4);
        U16(5); U16(0); U16(1); U16(8);

        // ContextFormat3: two input coverages (both at +18), two records calling lookup 0.
        U16(3); U16(2); U16(2); U16(18); U16(18);
        U16(0); U16(0); U16(1); U16(0);

        // Coverage format 1 with the glyph.
        U16(1); U16(1); U16(glyph);
        return b.ToArray();
    }

    private static byte[] WithGsub(byte[] font, byte[] gsub)
    {
        int padded = (font.Length + 3) & ~3;
        byte[] result = new byte[padded + gsub.Length];
        Array.Copy(font, result, font.Length);
        Array.Copy(gsub, 0, result, padded, gsub.Length);
        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int e = 12 + (i * 16);
            if (result[e] == 'G' && result[e + 1] == 'S' && result[e + 2] == 'U' && result[e + 3] == 'B')
            {
                WriteU32(result, e + 8, padded);
                WriteU32(result, e + 12, gsub.Length);
                return result;
            }
        }

        throw new InvalidOperationException("The font has no GSUB entry.");
    }

    private static void RenameTable(byte[] font, string from, string to)
    {
        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int e = 12 + (i * 16);
            if (font[e] == from[0] && font[e + 1] == from[1] && font[e + 2] == from[2] && font[e + 3] == from[3])
            {
                for (int k = 0; k < 4; k++)
                {
                    font[e + k] = (byte)to[k];
                }
            }
        }
    }

    private static (int Offset, int Length) FindTable(byte[] font, string tag)
    {
        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int e = 12 + (i * 16);
            if (font[e] == tag[0] && font[e + 1] == tag[1] && font[e + 2] == tag[2] && font[e + 3] == tag[3])
            {
                return (ReadU32(font, e + 8), ReadU32(font, e + 12));
            }
        }

        throw new InvalidOperationException(tag + " not found.");
    }

    private static int ReadU32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    private static void WriteU32(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }
}
