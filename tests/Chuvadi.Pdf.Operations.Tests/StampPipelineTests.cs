// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 (content), §12.3.3 (outlines), §11.6.4.4 (alpha)
// Tests for LA-24: single-write overlay + outline pipeline.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class StampPipelineTests
{
    private static byte[] BuildSource(int pages)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(PageSize.A4).DrawRectangle(80, 360, 300, 40, fill: new Color(220, 220, 255));
        }

        return builder.ToByteArray();
    }

    private static List<OutlineEntry> SampleOutline()
    {
        return new List<OutlineEntry>
        {
            new OutlineEntry("Chapter One", 0, new List<OutlineEntry> { new OutlineEntry("Section 1.1", 1) }),
            new OutlineEntry("Chapter Two", 2),
        };
    }

    [Fact]
    public void Pipeline_ComposesMultipleOverlaysInOneWrite_PreservesPages()
    {
        byte[] source = BuildSource(4);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        new StampPipeline(document)
            .AddTextWatermark("DRAFT", 60, ColorF.FromGray(0.5f), opacity: 0.18, rotationDegrees: 45)
            .AddPageNumbers(new StampNumbering { StartValue = 1 }, StampAnchor.BottomCenter, 10, ColorF.Black, template: "Page {number}")
            .AddHeaderFooter(new HeaderFooterOptions { Header = new BandText(left: "Chuvadi", center: "Confidential") })
            .Write(output);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(4);
    }

    [Fact]
    public void Pipeline_FoldsOutlineIntoSameWrite()
    {
        byte[] source = BuildSource(4);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        new StampPipeline(document)
            .AddPageNumbers(new StampNumbering { StartValue = 1 }, StampAnchor.BottomCenter, 10, ColorF.Black)
            .AddOutline(SampleOutline())
            .Write(output);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(reopened);

        outlines.Should().HaveCount(2);
        outlines[0].Title.Should().Be("Chapter One");
        outlines[0].Children.Should().HaveCount(1);
        outlines[0].Children[0].Title.Should().Be("Section 1.1");
        outlines[0].Children[0].DestinationPageIndex.Should().Be(1);
        outlines[1].Title.Should().Be("Chapter Two");
        outlines[1].DestinationPageIndex.Should().Be(2);
    }

    [Fact]
    public void Pipeline_WithEncryption_ProducesPasswordProtectedOutput()
    {
        byte[] source = BuildSource(3);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        new StampPipeline(document)
            .AddTextWatermark("SECRET", 50, ColorF.Black)
            .AddOutline(SampleOutline())
            .Write(output, EncryptionOptions.Aes256("pipe-pw"));

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, "pipe-pw", leaveOpen: true);
        reopened.PageCount.Should().Be(3);
        OutlineReader.GetOutlines(reopened).Should().HaveCount(2);
    }

    [Fact]
    public void Pipeline_NoSteps_WritesValidDocument()
    {
        byte[] source = BuildSource(2);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        new StampPipeline(document).Write(output);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(2);
    }
}
