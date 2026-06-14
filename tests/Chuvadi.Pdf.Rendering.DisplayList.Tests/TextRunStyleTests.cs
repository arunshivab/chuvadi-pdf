// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class TextRunStyleTests
{
    [Fact]
    public void Extract_PropagatesFontStyleAndSizeToRun()
    {
        List<DisplayListGlyph> glyphs = new() { new DisplayListGlyph(0, "A", 0, 0, 10) };
        TextOp op = new TextOp
        {
            FontKey = "F1",
            BaseFont = "Times-BoldItalic",
            FontSize = 14.0,
            Glyphs = glyphs,
            Transform = AffineMatrix.Identity,
            Style = new FontStyle("Times", 700, FontSlant.Italic, -15.0),
        };

        PageDisplayList list = new PageDisplayList(new RenderOp[] { op }, 612, 792, 0);

        IReadOnlyList<TextRun> runs = TextRunExtractor.Extract(list);

        runs.Should().HaveCount(1);
        TextRun run = runs[0];
        run.Unicode.Should().Be("A");
        run.FontFamily.Should().Be("Times");
        run.FontWeight.Should().Be(700);
        run.Slant.Should().Be(FontSlant.Italic);
        run.FontSize.Should().Be(14.0);
    }

    [Fact]
    public void Extract_DefaultStyle_WhenTextOpHasNone()
    {
        List<DisplayListGlyph> glyphs = new() { new DisplayListGlyph(0, "x", 0, 0, 8) };
        TextOp op = new TextOp
        {
            FontKey = "F1",
            BaseFont = "Helvetica",
            FontSize = 10.0,
            Glyphs = glyphs,
            Transform = AffineMatrix.Identity,
        };

        PageDisplayList list = new PageDisplayList(new RenderOp[] { op }, 612, 792, 0);

        TextRun run = TextRunExtractor.Extract(list)[0];
        run.FontWeight.Should().Be(400);
        run.Slant.Should().Be(FontSlant.Normal);
        run.FontSize.Should().Be(10.0);
    }
}
