// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class ShapedRunTests
{
    private static readonly ShapedGlyph[] TwoGlyphs =
    {
        new ShapedGlyph(3, 600, 0, 0, 0),
        new ShapedGlyph(5, 650, 40, 0, 0),
    };

    private static byte[] LoadFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));

    [Fact]
    public void DrawShapedRun_EmbedsFontViaRawGlyphIds()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddTrueTypeFont("Shaped", LoadFixture());
        PageBuilder page = builder.AddPage(PageSize.A4);

        // The font is used only through DrawShapedRun (no DrawText), so it can
        // only embed via the raw-glyph path.
        page.DrawShapedRun(new List<ShapedGlyph>(TwoGlyphs), 50, 100, "Shaped", 24, Color.FromHex("#000000"));

        string body = Encoding.Latin1.GetString(builder.ToByteArray());
        body.Should().Contain("CIDFontType2");
        body.Should().Contain("Identity-H");
    }

    [Fact]
    public void DrawShapedRun_UnregisteredFont_Throws()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);

        Action act = () => page.DrawShapedRun(new List<ShapedGlyph>(TwoGlyphs), 50, 100, "Missing", 24, Color.FromHex("#000000"));

        act.Should().Throw<InvalidOperationException>();
    }
}
