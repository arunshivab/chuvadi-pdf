// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Reader.Tests;

/// <summary>
/// Tests for the one-call <see cref="PdfRenderExtensions"/> facade: opening a
/// document and rendering a page to SVG, PNG, JPEG, BMP, and TIFF.
/// </summary>
public sealed class PdfRenderExtensionsTests
{
    private static PdfDocument OpenSample()
    {
        MemoryStream ms = TestBuilder.BuildPlainPdf();
        return PdfDocument.Open(ms, leaveOpen: false);
    }

    [Fact]
    public void RenderPageToSvg_ProducesSvgDocument()
    {
        using PdfDocument doc = OpenSample();
        string svg = doc.RenderPageToSvg(0);
        svg.Should().Contain("<svg");
    }

    [Fact]
    public void RenderPageToPng_ProducesPngSignature()
    {
        using PdfDocument doc = OpenSample();
        byte[] bytes = doc.RenderPageToPng(0);
        bytes.Length.Should().BeGreaterThan(8);
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'N');
        bytes[3].Should().Be((byte)'G');
    }

    [Fact]
    public void RenderPageToJpeg_ProducesJpegSignature()
    {
        using PdfDocument doc = OpenSample();
        byte[] bytes = doc.RenderPageToJpeg(0, dpi: 96, quality: 80);
        bytes.Length.Should().BeGreaterThan(2);
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
    }

    [Fact]
    public void RenderPageToBmp_ProducesBmpSignature()
    {
        using PdfDocument doc = OpenSample();
        byte[] bytes = doc.RenderPageToBmp(0, dpi: 96);
        bytes.Length.Should().BeGreaterThan(2);
        bytes[0].Should().Be((byte)'B');
        bytes[1].Should().Be((byte)'M');
    }

    [Fact]
    public void RenderPageToTiff_ProducesTiffSignature()
    {
        using PdfDocument doc = OpenSample();
        byte[] bytes = doc.RenderPageToTiff(0, dpi: 96);
        bytes.Length.Should().BeGreaterThan(4);
        // TIFF begins with "II" (little-endian) or "MM" (big-endian).
        bool littleEndian = bytes[0] == 0x49 && bytes[1] == 0x49;
        bool bigEndian = bytes[0] == 0x4D && bytes[1] == 0x4D;
        (littleEndian || bigEndian).Should().BeTrue();
    }

    [Fact]
    public void RenderToTiff_AllPages_ProducesTiff()
    {
        using PdfDocument doc = OpenSample();
        byte[] bytes = doc.RenderToTiff(dpi: 96);
        bytes.Length.Should().BeGreaterThan(4);
        bool littleEndian = bytes[0] == 0x49 && bytes[1] == 0x49;
        bool bigEndian = bytes[0] == 0x4D && bytes[1] == 0x4D;
        (littleEndian || bigEndian).Should().BeTrue();
    }

    [Fact]
    public void RenderPageToPng_StreamOverload_WritesBytes()
    {
        using PdfDocument doc = OpenSample();
        using MemoryStream output = new MemoryStream();
        doc.RenderPageToPng(0, output);
        output.Length.Should().BeGreaterThan(8);
    }

    [Fact]
    public void RenderPage_InvalidIndex_Throws()
    {
        using PdfDocument doc = OpenSample();
        Action act = () => doc.RenderPageToPng(99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RenderPage_NullDocument_Throws()
    {
        PdfDocument document = null!;
        Action act = () => document.RenderPageToSvg(0);
        act.Should().Throw<ArgumentNullException>();
    }
}
