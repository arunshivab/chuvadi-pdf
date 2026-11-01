// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.27: cpsp on by default again (D-68); Shape(byte[]) reuses the loaded face (D-70).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Fonts.Woff2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class ShapingDefaultsAndSpeedTests
{
    private const string Capitals = "HELLO WORLD";

    [Fact]
    public void CapitalSpacing_IsOnByDefault_AndCanBeSwitchedOff()
    {
        FontFace face = TestFonts.Lipi(LipiScript.Latin);

        IReadOnlyList<ShapedGlyph> byDefault = TextShaper.Shape(face, Capitals, LipiScript.Latin);
        IReadOnlyList<ShapedGlyph> on = TextShaper.Shape(face, Capitals, LipiScript.Latin, new ShapingFeatures { Cpsp = true });
        IReadOnlyList<ShapedGlyph> off = TextShaper.Shape(face, Capitals, LipiScript.Latin, new ShapingFeatures { Cpsp = false });

        byDefault.Should().Equal(on);
        byDefault.Sum(g => g.XAdvance).Should().BeGreaterThan(off.Sum(g => g.XAdvance), "the font's cpsp adds space between capitals");
    }

    [Fact]
    public void ByteOverload_GivesTheSameGlyphsAsTheFaceOverload()
    {
        byte[] bytes = LatinBytes();
        FontFace face = FontFace.Load(bytes);

        TextShaper.Shape(bytes, Capitals, LipiScript.Latin).Should().Equal(TextShaper.Shape(face, Capitals, LipiScript.Latin));
    }

    [Fact]
    public void ByteOverload_RepeatCallsCostAboutTheSameAsTheFaceOverload()
    {
        // Gate for D-70: in 3.26 every byte[] call loaded the face and built a
        // plan (about ten times the face overload). A ratio, not a time, so
        // the check holds on any CI machine.
        byte[] bytes = LipiTamilBytes();
        FontFace face = FontFace.Load(bytes);
        const string text = "தமிழ் எழுத்து";
        for (int i = 0; i < 50; i++)
        {
            TextShaper.Shape(bytes, text, LipiScript.Tamil);
            TextShaper.Shape(face, text, LipiScript.Tamil);
        }

        double viaBytes = Best(() => TextShaper.Shape(bytes, text, LipiScript.Tamil));
        double viaFace = Best(() => TextShaper.Shape(face, text, LipiScript.Tamil));

        viaBytes.Should().BeLessThan(viaFace * 2.0, "bytes {0:F1} µs against face {1:F1} µs per call", viaBytes, viaFace);
    }

    // Best of 5 rounds of 200 calls, in microseconds per call.
    private static double Best(Action call)
    {
        double best = double.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++)
            {
                call();
            }

            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000.0 / 200);
        }

        return best;
    }

    private static byte[] LatinBytes() =>
        Woff2Unpacker.Unpack(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiPi", "LiPi-Sans-Latin.woff2")));

    private static byte[] LipiTamilBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiPi-Sans-Tamil.ttf"));
}
