// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module

using System.IO;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class AuthoringTests
{
    [Fact]
    public void Document_WithOnePage_OneText_RoundTrips()
    {
        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4)
            .DrawText("Hello", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = doc.ToByteArray();

        bytes.Length.Should().BeGreaterThan(200);
        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void Document_MultiPage_DifferentSizes_Preserved()
    {
        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawText("p1", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);
        doc.AddPage(PageSize.Letter).DrawText("p2", 50, 50, StandardFonts.Helvetica, 12, Colors.Black);
        byte[] bytes = doc.ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(2);
        read.Pages[0].Width.Should().BeApproximately(595, 0.1);
        read.Pages[1].Width.Should().BeApproximately(612, 0.1);
    }

    [Fact]
    public void Table_AutoPaginates_OnOverflow()
    {
        var doc = PdfDocumentBuilder.Create();
        TableRenderResult result;
        result = doc.AddPage(PageSize.A4)
            .DrawTable(50, 100, 495)
            .AddColumn("Col", 1.0)
            .RowHeight(50)
            .AddRow("a").AddRow("b").AddRow("c").AddRow("d").AddRow("e")
            .AddRow("f").AddRow("g").AddRow("h").AddRow("i").AddRow("j")
            .AddRow("k").AddRow("l").AddRow("m").AddRow("n").AddRow("o")
            .AddRow("p").AddRow("q").AddRow("r").AddRow("s").AddRow("t")
            .Render();
        result.HasOverflow.Should().BeTrue("20 rows at 50pt each overflow A4");
        result.RemainingRows.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TextBlock_NarrowBox_Overflows()
    {
        var doc = PdfDocumentBuilder.Create();
        var p = doc.AddPage(PageSize.A4);
        var result = p.DrawTextBlock(
            "The quick brown fox jumps over the lazy dog repeatedly and again and again",
            x: 50, y: 50, width: 50, height: 30,
            font: StandardFonts.Helvetica, size: 12, color: Colors.Black);
        result.HasOverflow.Should().BeTrue();
        result.RemainingText.Should().NotBeEmpty();
    }

    [Fact]
    public void Header_AppliesPageNumbersToEveryPage()
    {
        var doc = PdfDocumentBuilder.Create()
            .SetHeader((p, num, total) => p.DrawText(
                $"P{num}/{total}", 50, 15, StandardFonts.Helvetica, 9, Colors.Black));
        doc.AddPage(PageSize.A4);
        doc.AddPage(PageSize.A4);
        doc.AddPage(PageSize.A4);
        byte[] bytes = doc.ToByteArray();

        string asText = System.Text.Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("P1/3");
        asText.Should().Contain("P2/3");
        asText.Should().Contain("P3/3");
    }

    [Fact]
    public void Hyperlink_EmitsLinkAnnotation()
    {
        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawHyperlink(
            "anthropic.com", 50, 50,
            StandardFonts.Helvetica, 12, Colors.LinkBlue, "https://anthropic.com");
        byte[] bytes = doc.ToByteArray();

        string asText = System.Text.Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/Subtype /Link");
        asText.Should().Contain("/URI");
        asText.Should().Contain("anthropic.com");
    }
}
