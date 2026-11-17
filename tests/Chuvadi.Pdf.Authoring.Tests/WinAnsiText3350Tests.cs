// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1 Annex D.2 (WinAnsiEncoding)
// PHASE: 3.35.0 (AUT-01) — text drawn with a standard font is encoded through
//        the WinAnsi table; a character the table lacks is an error that
//        points to embedding a font, and nothing is drawn.

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class WinAnsiText3350Tests
{
    private const string Punctuation = "Price €5 – “quoted” ‘single’ • more… — done";

    [Fact]
    public void DrawText_WinAnsiPunctuation_ReadsBack()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(Punctuation, 50, 50, StandardFonts.Helvetica, 12, Colors.Black);

        AuthoringTestKit.ExtractedText(builder.ToByteArray()).Should().Be(Punctuation);
    }

    [Fact]
    public void DrawText_LatinOneLetters_ReadBack()
    {
        const string text = "Café naïve Ångström üß";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText(text, 50, 50, StandardFonts.TimesRoman, 12, Colors.Black);

        AuthoringTestKit.ExtractedText(builder.ToByteArray()).Should().Be(text);
    }

    [Fact]
    public void DrawTextBlock_WinAnsiPunctuation_ReadsBack()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawTextBlock(Punctuation, 50, 50, 500, 100, StandardFonts.Helvetica, 12, Colors.Black);

        AuthoringTestKit.ExtractedText(builder.ToByteArray()).Should().Be(Punctuation);
    }

    [Fact]
    public void DrawText_Tamil_WithAStandardFont_ThrowsAndPointsToAddFont()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action draw = () => page.DrawText("வணக்கம்", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);

        draw.Should().Throw<ArgumentException>()
            .Where(e => e.Message.Contains("U+0BB5", StringComparison.Ordinal)
                && e.Message.Contains("AddFont", StringComparison.Ordinal));
    }

    [Fact]
    public void DrawTextBlock_Greek_WithAStandardFont_Throws()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action draw = () => page.DrawTextBlock("αβγ", 50, 50, 200, 100, StandardFonts.Courier, 12, Colors.Black);

        draw.Should().Throw<ArgumentException>().WithMessage("*AddFont*");
    }

    [Fact]
    public void DrawText_RejectedText_DrawsNothing()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);

        Action draw = () => page.DrawText("ok 中", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);

        draw.Should().Throw<ArgumentException>();
        AuthoringTestKit.ExtractedText(builder.ToByteArray()).Should().BeEmpty();
    }
}
