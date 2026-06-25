// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 (form XObjects), §8.4.4 (clipping)
// Tests for LA-11: N-up and saddle-stitch booklet imposition.

using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ImpositionTests
{
    private static byte[] BuildSource(int pages)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(PageSize.A4).DrawRectangle(40, 40, 515, 762, fill: new Color(220, 220, 255));
        }

        return builder.ToByteArray();
    }

    [Fact]
    public void NUp_TwoByTwo_ProducesOneSheetPerFourSourcePages()
    {
        byte[] source = BuildSource(8);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Imposition.NUp(output, document, new NUpOptions { Rows = 2, Columns = 2, Margin = 20, Gutter = 10 });

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(2);
    }

    [Fact]
    public void NUp_PartialLastSheet_RoundsUp()
    {
        byte[] source = BuildSource(5);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Imposition.NUp(output, document, new NUpOptions { Rows = 2, Columns = 2 });

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(2);
    }

    [Fact]
    public void NUp_ColumnMajorOrder_ProducesSameSheetCount()
    {
        byte[] source = BuildSource(4);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Imposition.NUp(output, document, new NUpOptions { Rows = 2, Columns = 2, Order = NUpOrder.ColumnMajor });

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Booklet_PadsToMultipleOfFour_ProducesTwoSidesPerSheet()
    {
        byte[] source = BuildSource(6);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Imposition.Booklet(output, document, new BookletOptions { Margin = 8 });

        // 6 pages pad to 8 -> 2 sheets -> 4 sides.
        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(4);
    }

    [Fact]
    public void NUp_WithEncryption_ProducesPasswordProtectedOutput()
    {
        byte[] source = BuildSource(4);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Imposition.NUp(output, document, new NUpOptions { Rows = 1, Columns = 2 }, EncryptionOptions.Aes256("imp-pw"));

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, "imp-pw", leaveOpen: true);
        reopened.PageCount.Should().Be(2);
    }
}
