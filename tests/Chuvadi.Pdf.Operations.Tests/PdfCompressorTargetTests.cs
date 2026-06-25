// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 (filters)
// Tests for LA-27: compress-to-target-size (binary search over JPEG quality).

using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PdfCompressorTargetTests
{
    private static byte[] BuildSource(int pages)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(PageSize.A4).DrawRectangle(40, 40, 515, 762, fill: new Color(200, 200, 240));
        }

        return builder.ToByteArray();
    }

    [Fact]
    public void CompressToTarget_LargeTarget_MeetsTargetAndUsesMaxQuality()
    {
        byte[] source = BuildSource(3);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressToTargetResult result = PdfCompressor.CompressToTarget(
            document, output, 10_000_000, new CompressToTargetOptions { MaxQuality = 90 });

        result.TargetMet.Should().BeTrue();
        result.QualityUsed.Should().Be(90);
        result.FinalSize.Should().Be(output.Length);
        result.SkipReason.Should().Be(CompressionSkipReason.None);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(3);
    }

    [Fact]
    public void CompressToTarget_UnreachableTarget_WritesSmallestAndReportsNotMet()
    {
        byte[] source = BuildSource(2);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressToTargetResult result = PdfCompressor.CompressToTarget(document, output, 1);

        result.TargetMet.Should().BeFalse();
        result.FinalSize.Should().Be(output.Length);
        output.Length.Should().BeGreaterThan(0);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(2);
    }

    [Fact]
    public void CompressToTarget_WritesValidReopenableDocument()
    {
        byte[] source = BuildSource(4);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        PdfCompressor.CompressToTarget(document, output, 500_000);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(4);
    }
}
