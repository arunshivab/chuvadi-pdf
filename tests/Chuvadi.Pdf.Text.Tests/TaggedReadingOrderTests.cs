// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — marked-content identifiers on fragments, and layout
//        extraction in a Tagged PDF's structure-tree order (D-104).

using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class TaggedReadingOrderTests
{
    // Two paragraphs, the upper one marked MCID 0 and the lower MCID 1, and
    // an unmarked page number at the bottom.
    private const string Content =
        "/P <</MCID 0>> BDC BT /F1 10 Tf 50 700 Td (Upper paragraph text) Tj ET EMC\n" +
        "/P <</MCID 1>> BDC BT /F1 10 Tf 50 600 Td (Lower paragraph text) Tj ET EMC\n" +
        "BT /F1 10 Tf 50 50 Td (Page 1) Tj ET\n";

    private const string Tagged = "/StructTreeRoot 6 0 R/MarkInfo<</Marked true>>";

    [Fact]
    public void Fragments_CarryTheirMarkedContentId()
    {
        using PdfDocument doc = TestPdf.Open(Content);

        List<TextFragment> f = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        f[0].MarkedContentId.Should().Be(0);
        f[1].MarkedContentId.Should().Be(1);
        f[2].MarkedContentId.Should().BeNull();
    }

    [Fact]
    public void Layout_FollowsTheStructureTree()
    {
        // The tree reads the lower paragraph first.
        using PdfDocument doc = TestPdf.Open(Content, Tagged, Tree("[8 0 R 7 0 R]"));

        string text = new TextExtractor(doc.Objects, ExtractionStrategy.Layout).ExtractText(doc.Pages[0]);

        TestPdf.Lines(text).Should().Equal("Lower paragraph text", "Upper paragraph text", "Page 1");
    }

    [Fact]
    public void MarkedContentReferences_AreFollowedToo()
    {
        string[] objects =
        {
            "<</Type/StructTreeRoot/K 7 0 R>>",
            "<</Type/StructElem/S/Document/P 6 0 R/K[<</Type/MCR/Pg 3 0 R/MCID 1>> <</Type/MCR/Pg 3 0 R/MCID 0>>]>>",
        };
        using PdfDocument doc = TestPdf.Open(Content, Tagged, objects);

        string text = new TextExtractor(doc.Objects, ExtractionStrategy.Layout).ExtractText(doc.Pages[0]);

        TestPdf.Lines(text)[0].Should().Be("Lower paragraph text");
    }

    [Fact]
    public void ATreeThatOrdersTooLittleOfThePage_IsNotUsed()
    {
        // Only the short MCID 1 is in the tree: geometry decides instead.
        string content =
            "/P <</MCID 0>> BDC BT /F1 10 Tf 50 700 Td (A long upper paragraph that the tree leaves out) Tj ET EMC\n" +
            "/P <</MCID 1>> BDC BT /F1 10 Tf 50 600 Td (Lower) Tj ET EMC\n";
        using PdfDocument doc = TestPdf.Open(content, Tagged, Tree("[8 0 R]"));
        TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);

        TestPdf.Lines(extractor.ExtractText(doc.Pages[0]))[0].Should().StartWith("A long upper");
        extractor.AnalyzeLayout(doc.Pages[0]).Source.Should().NotBe(ReadingOrderSource.Tagged);
    }

    [Fact]
    public void AnalyzeLayout_ReportsTaggedOrder()
    {
        using PdfDocument doc = TestPdf.Open(Content, Tagged, Tree("[8 0 R 7 0 R]"));

        new TextExtractor(doc.Objects).AnalyzeLayout(doc.Pages[0]).Should().BeSameAs(PageLayout.Tagged);
    }

    [Fact]
    public void RowsLayout_IgnoresTheTree()
    {
        using PdfDocument doc = TestPdf.Open(Content, Tagged, Tree("[8 0 R 7 0 R]"));

        string text = new TextExtractor(doc.Objects).ExtractText(doc.Pages[0], PageLayout.Rows);

        TestPdf.Lines(text)[0].Should().Be("Upper paragraph text");
    }

    [Fact]
    public void TaggedOrder_CanBeTurnedOff()
    {
        using PdfDocument doc = TestPdf.Open(Content, Tagged, Tree("[8 0 R 7 0 R]"));
        TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout) { UseTaggedOrder = false };

        TestPdf.Lines(extractor.ExtractText(doc.Pages[0]))[0].Should().Be("Upper paragraph text");
        extractor.AnalyzeLayout(doc.Pages[0]).Source.Should().Be(ReadingOrderSource.Rows);
    }

    [Fact]
    public void OperatorMode_KeepsTheStreamOrder()
    {
        using PdfDocument doc = TestPdf.Open(Content, Tagged, Tree("[8 0 R 7 0 R]"));

        string text = new TextExtractor(doc.Objects, ExtractionStrategy.Operator).ExtractText(doc.Pages[0]);

        TestPdf.Lines(text)[0].Should().Be("Upper paragraph text");
    }

    [Fact]
    public void ACyclicTree_IsWalkedOnce()
    {
        // Element 7's kids include itself.
        string[] objects =
        {
            "<</Type/StructTreeRoot/K 7 0 R>>",
            "<</Type/StructElem/S/P/P 6 0 R/Pg 3 0 R/K[1 7 0 R 0]>>",
        };
        using PdfDocument doc = TestPdf.Open(Content, Tagged, objects);

        string text = new TextExtractor(doc.Objects, ExtractionStrategy.Layout).ExtractText(doc.Pages[0]);

        TestPdf.Lines(text).Should().Equal("Lower paragraph text", "Upper paragraph text", "Page 1");
    }

    // The root (object 6) with the given kids; 7 holds MCID 0, 8 holds MCID 1.
    private static string[] Tree(string kids) => new[]
    {
        "<</Type/StructTreeRoot/K" + kids + ">>",
        "<</Type/StructElem/S/P/P 6 0 R/Pg 3 0 R/K 0>>",
        "<</Type/StructElem/S/P/P 6 0 R/Pg 3 0 R/K 1>>",
    };
}
