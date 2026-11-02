// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — font names in authored PDFs (D-105): an unknown name is
//        refused, and every name is written as a valid resource key.

using System;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class FontNameTests
{
    [Theory]
    [InlineData("NoSuchFont XYZ")]
    [InlineData("Arial")]
    [InlineData("LiPi Sans Latin")]
    [InlineData("helvetica")]
    public void DrawText_RefusesAFontItCannotWrite(string font)
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action draw = () => page.DrawText("Hello", 40, 40, font, 12, Colors.Black);

        ArgumentException error = draw.Should().Throw<ArgumentException>().Which;
        error.ParamName.Should().Be("font");
        error.Message.Should().Contain("Helvetica").And.Contain("Times-Roman").And.Contain("Lipi");
    }

    [Fact]
    public void DrawTextBlock_RefusesAFontItCannotWrite()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action draw = () => page.DrawTextBlock("Hello world", 40, 40, 200, 100, "Arial", 12, Colors.Black);

        draw.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("font");
    }

    [Fact]
    public void Lipi_WithoutUseLipiFonts_IsRefused_WithTheReason()
    {
        PageBuilder page = PdfDocumentBuilder.Create().AddPage(PageSize.A4);

        Action draw = () => page.DrawText("Hello", 40, 40, "Lipi", 12, Colors.Black);

        draw.Should().Throw<ArgumentException>().WithMessage("*UseLipiFonts()*");
    }

    [Fact]
    public void TheMessage_ListsTheRegisteredFonts()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddTrueTypeFont("Body Serif", LoadFixtureFont());
        PageBuilder page = builder.AddPage(PageSize.A4);

        Action draw = () => page.DrawText("Hello", 40, 40, "Body", 12, Colors.Black);

        draw.Should().Throw<ArgumentException>().WithMessage("*registered: Body Serif*");
    }

    [Fact]
    public void AFontNameWithASpace_IsWrittenAsAValidKey_AndItsTextReads()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddTrueTypeFont("Body Serif", LoadFixtureFont());
        builder.AddPage(PageSize.A4).DrawText("Spaced name", 40, 40, "Body Serif", 12, Colors.Black);
        byte[] pdf = builder.ToByteArray();

        Encoding.Latin1.GetString(pdf).Should().NotContain("/Body Serif");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf));
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Be("Spaced name");
        Fonts(doc).ContainsKey(PdfName.Intern("BodySerif")).Should().BeTrue();
    }

    [Fact]
    public void TwoNamesWithTheSameKey_GetDifferentKeys()
    {
        // "Helvetica-Bold" is keyed HelveticaBold, as before 3.30; a font
        // registered as "HelveticaBold" must not replace it.
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddTrueTypeFont("HelveticaBold", LoadFixtureFont());
        builder.AddPage(PageSize.A4)
            .DrawText("Standard", 40, 40, StandardFonts.HelveticaBold, 12, Colors.Black)
            .DrawText("Registered", 40, 80, "HelveticaBold", 12, Colors.Black);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(builder.ToByteArray()));

        PdfDictionary fonts = Fonts(doc);
        fonts.ContainsKey(PdfName.Intern("HelveticaBold")).Should().BeTrue();
        fonts.ContainsKey(PdfName.Intern("HelveticaBold2")).Should().BeTrue();
        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);
        text.Should().Contain("Standard").And.Contain("Registered");
    }

    [Fact]
    public void StandardFontKeys_AreUnchanged()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText("Plain", 40, 40, StandardFonts.TimesBoldItalic, 12, Colors.Black);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(builder.ToByteArray()));

        Fonts(doc).ContainsKey(PdfName.Intern("TimesBoldItalic")).Should().BeTrue();
    }

    private static PdfDictionary Fonts(PdfDocument doc)
    {
        PdfObjectStore store = doc.Objects;
        PdfDictionary resources = store.ResolveAs<PdfDictionary>(doc.Pages[0].Dictionary[PdfName.Intern("Resources")])!;
        return store.ResolveAs<PdfDictionary>(resources[PdfName.Intern("Font")])!;
    }

    private static byte[] LoadFixtureFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));
}
