// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (D-210 B) — one FontRenderer shared between threads

using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

/// <summary>
/// Runs its tests alone, never at the same time as another test class.
/// </summary>
/// <remarks>
/// The shared-renderer test keeps 8 threads busy for about two seconds. Run
/// beside the other classes, it starved the timing-sensitive
/// <c>RenderGuardTests.Rasterize_StopsSoonAfterCancellation</c> (1.5 s
/// limit), which then failed 3 runs in 6 in the sandbox and on the macOS CI
/// runner.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CpuHeavyCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "CPU-heavy tests, run alone";
}

[Collection(CpuHeavyCollection.Name)]
public sealed class SharedFontRenderer3312Tests
{
    [Fact]
    public void FontRenderer_SharedBetweenThreads_GivesTheSameOutlinesAsOneThread()
    {
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Tamil-Regular.ttf");
        int glyphs = FontFace.Load(program).GlyphCount;

        // Threads ask for different sizes and hinting modes, as LiPi's shared
        // renderers do, so the reused hinting interpreter is rebuilt while
        // other threads use it. That is what threw before 3.31.2.
        (int Ppem, bool Light)[] modes = { (16, false), (17, true), (18, false), (16, true) };

        // The expected outlines, from a renderer used by one thread only.
        FontRenderer single = new FontRenderer(program);
        string[] expectedPlain = new string[glyphs];
        string?[,] expectedHinted = new string?[modes.Length, glyphs];
        for (int g = 0; g < glyphs; g++)
        {
            expectedPlain[g] = Describe(single.GetGlyphOutline(g));
            for (int m = 0; m < modes.Length; m++)
            {
                expectedHinted[m, g] = DescribeOrNull(single.GetHintedGlyphOutline(g, modes[m].Ppem, modes[m].Light));
            }
        }

        // A fresh shared renderer each round, so every round races on empty
        // caches.
        int mismatches = 0;
        for (int round = 0; round < 40; round++)
        {
            FontRenderer shared = new FontRenderer(program);
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
            {
                int m = t % modes.Length;
                for (int i = 0; i < glyphs; i++)
                {
                    int g = ((i * 7) + (t * 131)) % glyphs;
                    if (DescribeOrNull(shared.GetHintedGlyphOutline(g, modes[m].Ppem, modes[m].Light)) != expectedHinted[m, g])
                    {
                        Interlocked.Increment(ref mismatches);
                    }

                    if (Describe(shared.GetGlyphOutline(g)) != expectedPlain[g])
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                }
            });
        }

        mismatches.Should().Be(0);
    }

    private static string? DescribeOrNull(GlyphOutline? outline) => outline is null ? null : Describe(outline);

    private static string Describe(GlyphOutline outline)
    {
        RectangleF bounds = outline.Outline.EndpointBounds();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{outline.Outline.Count}|{bounds.X:R}|{bounds.Y:R}|{bounds.Width:R}|{bounds.Height:R}|{outline.Metrics.AdvanceWidth:R}");
    }
}
