// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — SVG text in embedded fonts is placed at the PDF's own glyph
//        positions, so it lines up with search highlights and selection.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgTextPositionTests
{
    private static readonly Regex TextElement = new(
        "<text[^>]*?\\sx=\"([^\"]*)\"[^>]*font-family=\"&quot;chuvadi_[^\"]*\"[^>]*>([^<]*)</text>",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryCharacterInAnEmbeddedFont_HasItsOwnPosition()
    {
        using PdfDocument doc = OpenFixture();

        string svg = new SvgRenderer().RenderPage(doc, 0);

        MatchCollection texts = TextElement.Matches(svg);
        texts.Count.Should().BeGreaterThan(0);
        foreach (Match m in texts)
        {
            int positions = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            int characters = WebUtility.HtmlDecode(m.Groups[2].Value).Length;
            positions.Should().Be(characters, "each character of \"{0}\" is placed by the PDF", m.Groups[2].Value);
        }
    }

    [Fact]
    public void ThePositions_AreThePdfGlyphPositions()
    {
        using PdfDocument doc = OpenFixture();
        List<TextOp> runs = DisplayListBuilder.Build(doc, 0).OfType<TextOp>()
            .Where(t => t.Glyphs.Any(g => g.Unicode.Length > 0))
            .ToList();

        string svg = new SvgRenderer().RenderPage(doc, 0);

        MatchCollection texts = TextElement.Matches(svg);
        texts.Count.Should().Be(runs.Count);
        for (int i = 0; i < runs.Count; i++)
        {
            double[] expected = runs[i].Glyphs.Where(g => g.Unicode.Length == 1).Select(g => g.X).ToArray();
            double[] actual = texts[i].Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            actual.Should().Equal(expected, (a, e) => Math.Abs(a - e) < 0.001);
        }
    }

    private static PdfDocument OpenFixture() =>
        PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "testPDF_JBIG2.pdf"));
}
