// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.26: shaped Indic runs drawn with DrawShapedRun and per-cluster
// /ActualText extract as written, with no space inside a word.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping;
using FluentAssertions;
using Xunit;
using static Chuvadi.Pdf.Authoring.Tests.AuthoringTestKit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class IndicExtractionTests
{
    private static byte[] TamilFont() =>
        File.ReadAllBytes(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "LiPi-Sans-Tamil.ttf"));

    [Theory]
    [InlineData("கொடு")]
    [InlineData("தமிழ்")]
    [InlineData("கொடு தமிழ்")]
    public void TamilShapedRun_ExtractsWithoutExtraSpaces(string text)
    {
        byte[] font = TamilFont();
        IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(FontFace.Load(font), text, LipiScript.Tamil);
        int[] starts = glyphs.Select(g => g.Cluster).ToArray();

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", font);
        builder.AddPage(PageSize.A4).DrawShapedRun(glyphs, text, starts, 50, 100, "T", 24, Colors.Black);

        ExtractedText(builder.ToByteArray()).Should().Be(text);
    }
}
