// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.26: TextShaper against HarfBuzz. Fixtures/HarfBuzzConformance.tsv holds
// HarfBuzz 15.0.0 results (glyph, cluster, advance, offsets) for a sample of
// the conformance corpora in tools/shaping-data; every row must match exactly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Fonts.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class HarfBuzzConformanceTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Theory]
    [InlineData("Tamil")]
    [InlineData("Devanagari")]
    [InlineData("Bengali")]
    [InlineData("Gurmukhi")]
    [InlineData("Gujarati")]
    [InlineData("Odia")]
    [InlineData("Telugu")]
    [InlineData("Kannada")]
    [InlineData("Malayalam")]
    [InlineData("Latin")]
    public void Script_MatchesHarfBuzz(string scriptName)
    {
        (int rows, List<string> failures) = Run(Path.Combine(Fixtures, "HarfBuzzConformance.tsv"), scriptName);

        rows.Should().BeGreaterThan(40);
        failures.Should().BeEmpty();
    }

    [Fact]
    public void FullCorpus_MatchesHarfBuzz_WhenProvided()
    {
        // CHUVADI_HB_CORPUS points at a file from tools/shaping-data/hb_conformance.py full.
        string? path = Environment.GetEnvironmentVariable("CHUVADI_HB_CORPUS");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        (int rows, List<string> failures) = Run(path, null);

        rows.Should().BeGreaterThan(0);
        failures.Take(20).Should().BeEmpty("{0} of {1} rows differ", failures.Count, rows);
    }

    private static (int Rows, List<string> Failures) Run(string file, string? scriptName)
    {
        List<string> failures = new List<string>();
        int rows = 0;
        foreach (string line in File.ReadLines(file))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] p = line.Split('\t');
            if (scriptName is not null && p[0] != scriptName)
            {
                continue;
            }

            rows++;
            LipiScript script = Enum.Parse<LipiScript>(p[0]);
            string text = string.Concat(p[1].Split(' ').Select(h => char.ConvertFromUtf32(int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            FontFace face = TestFonts.Face(p[2]);
            double k = face.UnitsPerEm / 1000.0;
            string actual = string.Join(' ', TextShaper.Shape(face, text, script).Select(g => string.Create(
                CultureInfo.InvariantCulture,
                $"{g.GlyphId}:{g.Cluster}:{Math.Round(g.XAdvance * k)}:{Math.Round(g.XOffset * k)}:{Math.Round(g.YOffset * k)}")));
            if (actual != p[3])
            {
                failures.Add($"{p[1]}: expected {p[3]}, got {actual}");
            }
        }

        return (rows, failures);
    }
}
