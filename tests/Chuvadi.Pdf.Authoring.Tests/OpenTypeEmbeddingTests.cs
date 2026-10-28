// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.25: embedding and subsetting CFF-based OpenType (.otf) fonts and faces of
// TrueType collections (.ttc), with the synthetic fixtures.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;
using static Chuvadi.Pdf.Authoring.Tests.AuthoringTestKit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class OpenTypeEmbeddingTests
{
    public static TheoryData<string, int, string, string> Fonts => new TheoryData<string, int, string, string>
    {
        { "ChuvadiTest-Cff.otf", 0, "CIDFontType0", "FontFile3" },
        { "ChuvadiTest-Cid.otf", 0, "CIDFontType0", "FontFile3" },
        { "ChuvadiTest-Glyf.ttf", 0, "CIDFontType2", "FontFile2" },
        { "ChuvadiTest.ttc", 0, "CIDFontType2", "FontFile2" },
        { "ChuvadiTest.ttc", 1, "CIDFontType0", "FontFile3" },
    };

    [Theory]
    [MemberData(nameof(Fonts))]
    public void AddFont_EmbedsTheFace_RendersAndExtracts(string file, int face, string cidType, string fontFile)
    {
        byte[] pdf = Build(file, face, "AH");

        string body = Encoding.Latin1.GetString(pdf);
        body.Should().Contain("/" + cidType).And.Contain("/" + fontFile);
        if (fontFile == "FontFile3")
        {
            body.Should().Contain("/CIDFontType0C");
            body.Should().NotContain("/CIDToGIDMap", "a CIDFontType0 maps CIDs through its CFF charset");
        }

        ExtractedText(pdf).Should().Be("AH");

        // Baseline at y = 150, 100 pt text from x = 50: A's left bar spans
        // x 55-65, H's right bar x 158-168; the space inside A at (80, 100)
        // and inside H at (135, 100) is empty.
        PixelBuffer px = Raster(pdf);
        IsDark(px, 60, 120).Should().BeTrue("A's left bar is drawn");
        IsDark(px, 163, 120).Should().BeTrue("H's right bar is drawn");
        IsWhite(px, 80, 100).Should().BeTrue("A is not filled in");
        IsWhite(px, 135, 100).Should().BeTrue("H is not filled in");
    }

    [Theory]
    [InlineData("ChuvadiTest-Cff.otf")]
    [InlineData("ChuvadiTest-Cid.otf")]
    public void CffSubset_KeepsOnlyTheDrawnGlyphs_WithTheirOutlinesAndCids(string file)
    {
        byte[] pdf = Build(file, 0, "AHx");

        CffLoader subset = new CffLoader(EmbeddedProgram(pdf, "FontFile3"));
        CffLoader original = new CffLoader(CffTable(Font(file)));

        subset.IsCidFont.Should().BeTrue();
        subset.NumGlyphs.Should().Be(4, ".notdef, A, H and x");
        subset.CidToGid.Keys.Should().BeEquivalentTo(new[] { 0, GidA, GidH, GidX });
        foreach (int cid in new[] { GidA, GidH, GidX })
        {
            GlyphOutline kept = subset.GetGlyphOutline(subset.CidToGid[cid]);
            GlyphOutline source = original.GetGlyphOutline(cid);
            kept.Metrics.Bounds.Should().Be(source.Metrics.Bounds, "glyph {0} keeps its outline", cid);
            kept.Metrics.AdvanceWidth.Should().Be(source.Metrics.AdvanceWidth);
        }
    }

    [Fact]
    public void CffSubset_EmptiesUnusedSubroutines()
    {
        // 'x' and 'H' call local subrs 2 and 1, 'B' a global subr; drawing only
        // "A" (local subr 0) leaves the others unused.
        byte[] onlyA = EmbeddedProgram(Build("ChuvadiTest-Cff.otf", 0, "A"), "FontFile3");
        byte[] all = EmbeddedProgram(Build("ChuvadiTest-Cff.otf", 0, "ABHx"), "FontFile3");

        onlyA.Length.Should().BeLessThan(all.Length);
        new CffLoader(onlyA).GetGlyphOutline(1).Metrics.Bounds.Should()
            .Be(new CffLoader(all).GetGlyphOutline(1).Metrics.Bounds, "A still draws through its subroutine");
    }

    [Fact]
    public void FontEmbedder_GlyphText_AddsToUnicodeForUnmappedGlyphs()
    {
        FontFace face = FontFace.Load(Font("ChuvadiTest-Cff.otf"));
        int next = 1;

        EmbeddedFontObjects objects = FontEmbedder.Build(
            face,
            new[] { (int)'A' },
            new Dictionary<int, string> { [GidFi] = "fi" },
            "ChuvadiTest",
            () => new PdfObjectId(next++, 0));

        string cmap = objects.Objects
            .Select(o => o.Value)
            .OfType<PdfStream>()
            .Select(s => Encoding.ASCII.GetString(s.RawBytes))
            .Single(s => s.Contains("beginbfchar", StringComparison.Ordinal));
        cmap.Should().Contain("<0002> <0041>").And.Contain("<0009> <00660069>");
    }

    [Fact]
    public void AddTrueTypeFont_AcceptsOtfAndCollections()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddTrueTypeFont("Otf", Font("ChuvadiTest-Cff.otf"))
            .AddTrueTypeFont("Ttc", Font("ChuvadiTest.ttc"));
        PageBuilder page = builder.AddPage(PageSize.A4);
        page.DrawText("AH", 50, 50, "Otf", 20, Colors.Black);
        page.DrawText("AH", 50, 100, "Ttc", 20, Colors.Black);

        ExtractedText(builder.ToByteArray()).Should().Contain("AH");
    }

    [Fact]
    public void AddFont_BadFaceIndex_Throws()
    {
        Action act = () => PdfDocumentBuilder.Create().AddFont("X", Font("ChuvadiTest.ttc"), 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] Build(string file, int face, string text)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("Test", Font(file), face);
        builder.AddPage(PageSize.A4).DrawText(text, 50, 50, "Test", 100, Colors.Black);
        return builder.ToByteArray();
    }

    // The decoded font program of the page's first font.
    private static byte[] EmbeddedProgram(byte[] pdf, string key)
    {
        using PdfDocument doc = Open(pdf);
        PdfDictionary fonts = doc.Objects.ResolveAs<PdfDictionary>(doc.Pages[0].Resources!.GetAs<PdfPrimitive>(PdfName.Intern("Font"))!)!;
        PdfDictionary type0 = doc.Objects.ResolveAs<PdfDictionary>(fonts.Values.First())!;
        PdfArray descendants = doc.Objects.ResolveAs<PdfArray>(type0.GetAs<PdfPrimitive>(PdfName.Intern("DescendantFonts"))!)!;
        PdfDictionary cidFont = doc.Objects.ResolveAs<PdfDictionary>(descendants[0])!;
        PdfDictionary descriptor = doc.Objects.ResolveAs<PdfDictionary>(cidFont.GetAs<PdfPrimitive>(PdfName.Intern("FontDescriptor"))!)!;
        PdfStream stream = doc.Objects.ResolveAs<PdfStream>(descriptor.GetAs<PdfPrimitive>(PdfName.Intern(key))!)!;
        return stream.Filter is PdfName filter
            ? new FilterPipeline().Decode(filter.Value, stream.RawBytes)
            : stream.RawBytes;
    }

    private static byte[] CffTable(byte[] font)
    {
        int numTables = (font[4] << 8) | font[5];
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            if (Encoding.ASCII.GetString(font, record, 4) == "CFF ")
            {
                int offset = (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
                int length = (font[record + 12] << 24) | (font[record + 13] << 16) | (font[record + 14] << 8) | font[record + 15];
                return font.AsSpan(offset, length).ToArray();
            }
        }

        throw new InvalidOperationException("No CFF table.");
    }
}
