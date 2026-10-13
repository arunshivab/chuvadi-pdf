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

    [Fact]
    public void CompressToTarget_UnreachableTarget_NeverRasterizesByDefault()
    {
        byte[] source = BuildSource(2);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressToTargetResult result = PdfCompressor.CompressToTarget(document, output, 1);

        result.UsedRasterizeFallback.Should().BeFalse(
            "rasterizing destroys selectable text, so it must be opt-in");
        result.RasterizeDpiUsed.Should().Be(0);
    }

    // A page whose content stream is large and incompressible (thousands of
    // randomly coloured rectangles) but whose rendered appearance is a small
    // blob — the vector floor is far above what a low-DPI raster needs.
    private static byte[] BuildDenseVectorSource()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        PageBuilder page = builder.AddPage(PageSize.A4);

        // Deterministic LCG so the test is reproducible; random colours defeat
        // Flate and keep the vector floor high.
        uint seed = 123456789;
        uint Next()
        {
            seed = (seed * 1664525u) + 1013904223u;
            return seed;
        }

        for (int i = 0; i < 8000; i++)
        {
            double x = 100 + (Next() % 500) / 10.0;
            double y = 100 + (Next() % 500) / 10.0;
            Color fill = new Color(
                (byte)(Next() % 256), (byte)(Next() % 256), (byte)(Next() % 256));
            page.DrawRectangle(x, y, 2, 2, fill: fill);
        }

        return builder.ToByteArray();
    }

    [Fact]
    public void CompressToTarget_RasterizeFallback_MeetsVectorFloorTarget()
    {
        // The quality search cannot shrink a vector-only document (no images
        // to re-encode), so an aggressive target is unreachable — until the
        // opted-in fallback rasterizes the page, whose rendered appearance is
        // a small blob that compresses to a few KB of JPEG.
        byte[] source = BuildDenseVectorSource();
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream vectorFloor = new MemoryStream();
        PdfCompressor.CompressToTarget(document, vectorFloor, 1);

        long target = 30_000;
        vectorFloor.Length.Should().BeGreaterThan(target,
            "the test premise requires the vector floor to exceed the target");

        using MemoryStream output = new MemoryStream();
        CompressToTargetResult result = PdfCompressor.CompressToTarget(
            document, output, target, new CompressToTargetOptions
            {
                AllowRasterizeFallback = true,
                RasterizeDpi = 72,
            });

        result.UsedRasterizeFallback.Should().BeTrue(
            "the vector floor exceeds the target, so the opted-in fallback engages");
        result.TargetMet.Should().BeTrue();
        result.RasterizeDpiUsed.Should().Be(72);
        result.FinalSize.Should().Be(output.Length);
        output.Length.Should().BeLessThanOrEqualTo(target);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.PageCount.Should().Be(1, "the raster output keeps one page per source page");
    }

    [Fact]
    public void CompressToTarget_RasterizeFallback_NotUsedWhenQualitySearchSucceeds()
    {
        byte[] source = BuildSource(2);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressToTargetResult result = PdfCompressor.CompressToTarget(
            document, output, 10_000_000, new CompressToTargetOptions
            {
                AllowRasterizeFallback = true,
            });

        result.TargetMet.Should().BeTrue();
        result.UsedRasterizeFallback.Should().BeFalse(
            "the fallback only engages when the quality search cannot meet the target");
    }
}
