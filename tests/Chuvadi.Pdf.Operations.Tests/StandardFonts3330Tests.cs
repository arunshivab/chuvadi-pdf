// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard 14 fonts), §9.6.3 (TrueType fonts)
// PHASE: 3.33.0 (M1, D-302, D-307/D-308) — PdfDocumentBuilder.EmbedStandardFonts
//        and the StandardFontEmbedder operation.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class StandardFonts3330Tests
{
    [Fact]
    public void Builder_WithoutTheOption_NamesTheFontAsBefore()
    {
        using PdfDocument document = Open(TwoPages(embed: false));

        List<PdfDictionary> fonts = PageFonts(document).ToList();
        fonts.Should().OnlyContain(f => f.GetName(PdfName.Intern("Subtype"))!.Value == "Type1");
        fonts.Should().OnlyContain(f => !f.ContainsKey(PdfName.Intern("FontDescriptor")));
    }

    [Fact]
    public void Builder_EmbedStandardFonts_EmbedsOneSharedSubstitutePerFace()
    {
        using PdfDocument document = Open(TwoPages(embed: true));

        List<PdfDictionary> helvetica = PageFonts(document)
            .Where(f => f.GetName(PdfName.Intern("Subtype"))!.Value == "TrueType")
            .ToList();
        helvetica.Should().HaveCount(2);
        helvetica[0].Should().BeSameAs(helvetica[1]);
        helvetica[0].GetName(PdfName.Intern("BaseFont"))!.Value.Should().EndWith("+LiberationSans-Regular");
        helvetica[0].GetName(PdfName.Intern("Encoding"))!.Value.Should().Be("WinAnsiEncoding");
        PdfDictionary descriptor = (PdfDictionary)document.Objects.Resolve(helvetica[0][PdfName.Intern("FontDescriptor")]);
        descriptor.ContainsKey(PdfName.Intern("FontFile2")).Should().BeTrue();

        new TextExtractor(document.Objects).ExtractText(document.Pages[0]).Should().Contain("Hello café");
    }

    [Fact]
    public void Builder_EmbedStandardFonts_LeavesSymbolNamedOnly()
    {
        using PdfDocument document = Open(TwoPages(embed: true));

        PdfDictionary symbol = PageFonts(document)
            .Single(f => f.GetName(PdfName.Intern("BaseFont"))!.Value == "Symbol");
        symbol.GetName(PdfName.Intern("Subtype"))!.Value.Should().Be("Type1");
    }

    [Fact]
    public void Embed_EmbedsTheStandardFontsAndReportsSymbol()
    {
        using PdfDocument source = Open(TwoPages(embed: false));
        using MemoryStream output = new MemoryStream();

        StandardFontEmbedResult result = StandardFontEmbedder.Embed(source, output);

        result.EmbeddedFonts.Should().Equal("Helvetica");
        result.NotEmbedded.Should().ContainSingle().Which.Should().Contain("'Symbol'");
        output.Position = 0;
        using PdfDocument embedded = PdfDocument.Open(output);
        PageFonts(embedded).Count(f => f.GetName(PdfName.Intern("Subtype"))!.Value == "TrueType").Should().Be(2);
        new TextExtractor(embedded.Objects).ExtractText(embedded.Pages[1])
            .Should().Be(new TextExtractor(source.Objects).ExtractText(source.Pages[1]));
    }

    [Fact]
    public void Embed_DoesNotChangeTheSourceDocument()
    {
        using PdfDocument source = Open(TwoPages(embed: false));
        using MemoryStream output = new MemoryStream();

        StandardFontEmbedder.Embed(source, output);

        PageFonts(source).Should().OnlyContain(f => f.GetName(PdfName.Intern("Subtype"))!.Value == "Type1");
    }

    [Fact]
    public void Embed_AliasesAreEmbeddedUnlessTurnedOff()
    {
        using PdfDocument withAlias = Open(TwoPages(embed: false));
        foreach (PdfDictionary font in PageFonts(withAlias).Where(f => f.GetName(PdfName.Intern("BaseFont"))!.Value == "Helvetica"))
        {
            font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("ArialMT"));
        }

        using MemoryStream first = new MemoryStream();
        StandardFontEmbedder.Embed(withAlias, first).EmbeddedFonts.Should().Equal("ArialMT");

        using MemoryStream second = new MemoryStream();
        StandardFontEmbedResult off = StandardFontEmbedder.Embed(
            withAlias, second, new StandardFontEmbedOptions { IncludeAliases = false });
        off.EmbeddedFonts.Should().BeEmpty();
        off.NotEmbedded.Should().Contain(m => m.Contains("'ArialMT'"));
    }

    [Fact]
    public void Embed_EncryptedDocument_ThrowsUnlessAllowed()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawText("Secret", 72, 72, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = builder.ToByteArray(EncryptionOptions.Aes256("user", "owner"));
        using PdfDocument source = PdfDocument.Open(new MemoryStream(bytes), "user");

        using MemoryStream output = new MemoryStream();
        FluentActions.Invoking(() => StandardFontEmbedder.Embed(source, output))
            .Should().Throw<OperationsException>();

        StandardFontEmbedder.Embed(source, output, new StandardFontEmbedOptions { AllowEncryptedInput = true })
            .EmbeddedFonts.Should().Equal("Helvetica");
    }

    private static byte[] TwoPages(bool embed)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        if (embed)
        {
            builder.EmbedStandardFonts();
        }

        builder.AddPage(PageSize.A4)
            .DrawText("Hello café", 72, 72, StandardFonts.Helvetica, 12, Colors.Black)
            .DrawText("abc", 72, 120, StandardFonts.Symbol, 12, Colors.Black);
        builder.AddPage(PageSize.A4).DrawText("Second page", 72, 72, StandardFonts.Helvetica, 12, Colors.Black);
        return builder.ToByteArray();
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes));

    private static IEnumerable<PdfDictionary> PageFonts(PdfDocument document)
    {
        for (int i = 0; i < document.PageCount; i++)
        {
            PdfDictionary fonts = (PdfDictionary)document.Objects.Resolve(document.Pages[i].Resources![PdfName.Intern("Font")]);
            foreach (PdfPrimitive value in fonts.Values)
            {
                yield return (PdfDictionary)document.Objects.Resolve(value);
            }
        }
    }
}
