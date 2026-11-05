// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.27: cpsp on by default again (D-68); Shape(byte[]) reuses the loaded face (D-70).

// Fonts.Bundled and Fonts.Woff2 are deprecated (3.31.0) but still ship and are still tested.
#pragma warning disable CS0618

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
    public void ByteOverload_RepeatCallsAllocateNoMoreThanTheFaceOverload()
    {
        // Gate for D-70, deterministic: in 3.26 every byte[] call loaded the
        // face again (hundreds of KB of tables). With the face reused, a
        // repeat call allocates what the FontFace overload does.
        byte[] bytes = LipiTamilBytes();
        FontFace face = FontFace.Load(bytes);
        TextShaper.Shape(bytes, Tamil, LipiScript.Tamil);
        TextShaper.Shape(face, Tamil, LipiScript.Tamil);

        long viaBytes = Allocated(() => TextShaper.Shape(bytes, Tamil, LipiScript.Tamil));
        long viaFace = Allocated(() => TextShaper.Shape(face, Tamil, LipiScript.Tamil));

        viaBytes.Should().BeLessThanOrEqualTo(viaFace + 1024, "bytes {0} B against face {1} B per call", viaBytes, viaFace);
    }

    [Fact]
    public void ByteOverload_RepeatCallsCostAtMostTwiceTheFaceOverload()
    {
        // Gate for D-70 (ratio of at most 2x). A ratio, not a time, so it
        // holds on any machine. The two overloads are timed in alternating
        // rounds after a long warm-up, so JIT tiering and machine noise fall
        // on both alike, and the medians are compared. (3.27's first version
        // timed one after the other after 50 warm-up calls; on a CI runner the
        // first-timed overload was still running unoptimised code.)
        byte[] bytes = LipiTamilBytes();
        FontFace face = FontFace.Load(bytes);
        for (int i = 0; i < 2000; i++)
        {
            TextShaper.Shape(bytes, Tamil, LipiScript.Tamil);
            TextShaper.Shape(face, Tamil, LipiScript.Tamil);
        }

        List<double> viaBytes = new List<double>();
        List<double> viaFace = new List<double>();
        for (int round = 0; round < 15; round++)
        {
            viaBytes.Add(Time(() => TextShaper.Shape(bytes, Tamil, LipiScript.Tamil)));
            viaFace.Add(Time(() => TextShaper.Shape(face, Tamil, LipiScript.Tamil)));
        }

        double b = Median(viaBytes);
        double f = Median(viaFace);
        b.Should().BeLessThan(f * 2.0, "bytes {0:F1} µs against face {1:F1} µs per call (medians of 15 rounds)", b, f);
    }

    private const string Tamil = "தமிழ் எழுத்து";

    // Bytes allocated by 100 calls, per call.
    private static long Allocated(Action call)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            call();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
    }

    // One round of 100 calls, in microseconds per call.
    private static double Time(Action call)
    {
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            call();
        }

        return sw.Elapsed.TotalMilliseconds * 1000.0 / 100;
    }

    private static double Median(List<double> values)
    {
        List<double> sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static byte[] LatinBytes() =>
        Woff2Unpacker.Unpack(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiPi", "LiPi-Sans-Latin.woff2")));

    private static byte[] LipiTamilBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiPi-Sans-Tamil.ttf"));
}
