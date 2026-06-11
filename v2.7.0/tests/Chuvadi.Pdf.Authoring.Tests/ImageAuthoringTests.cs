// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images, §11.6.5.2 — Soft-mask images
// PHASE: Phase 2.7 — Image → PDF (authoring pipeline tests)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class ImageAuthoringTests
{
    // ── Embedding via PageBuilder ─────────────────────────────────────────

    [Fact]
    public void DrawImage_RgbaPng_EmitsSoftMask()
    {
        ImageFrame frame = ImageFrame.Create(2, 2, ImageColorFormat.Rgba32);
        frame.Pixels.SetPixelBgra(0, 0, 10, 20, 30, 128);
        frame.Pixels.SetPixelBgra(1, 0, 40, 50, 60, 255);
        frame.Pixels.SetPixelBgra(0, 1, 70, 80, 90, 0);
        frame.Pixels.SetPixelBgra(1, 1, 100, 110, 120, 255);
        byte[] png = EncodePng(frame, includeAlpha: true);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(png, 50, 50, 100, 100);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/SMask");
        asText.Should().Contain("/DeviceRGB");
        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void DrawImage_OpaqueTruecolourPng_EmbedsWithoutSoftMask()
    {
        ImageFrame frame = ImageFrame.Create(2, 1, ImageColorFormat.Rgb24);
        frame.Pixels.SetPixelBgra(0, 0, 1, 2, 3, 255);
        frame.Pixels.SetPixelBgra(1, 0, 4, 5, 6, 255);
        byte[] png = EncodePng(frame, includeAlpha: false);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(png, 50, 50, 100, 50);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().NotContain("/SMask");
        asText.Should().Contain("/FlateDecode");
        asText.Should().Contain("/DeviceRGB");
    }

    [Fact]
    public void DrawImage_GrayFrame_EmbedsAsDeviceGray()
    {
        ImageFrame frame = ImageFrame.Create(2, 1, ImageColorFormat.Gray8);
        frame.Pixels.SetPixelBgra(0, 0, 40, 40, 40, 255);
        frame.Pixels.SetPixelBgra(1, 0, 200, 200, 200, 255);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(frame, 50, 50, 100, 50);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DeviceGray");
    }

    [Fact]
    public void DrawImage_Bmp_Embeds()
    {
        ImageFrame frame = ImageFrame.Create(2, 2, ImageColorFormat.Rgb24);
        frame.Pixels.SetPixelBgra(0, 0, 255, 0, 0, 255);
        frame.Pixels.SetPixelBgra(1, 0, 0, 255, 0, 255);
        frame.Pixels.SetPixelBgra(0, 1, 0, 0, 255, 255);
        frame.Pixels.SetPixelBgra(1, 1, 255, 255, 255, 255);
        using MemoryStream ms = new();
        BmpEncoder.Encode(frame, ms);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(ms.ToArray(), 50, 50, 100, 100);
        byte[] bytes = doc.ToByteArray();

        using PdfDocument read = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        read.PageCount.Should().Be(1);
        Encoding.Latin1.GetString(bytes).Should().Contain("/DeviceRGB");
    }

    [Fact]
    public void DrawImage_JpegThreeComponent_PassesThroughDct()
    {
        byte[] jpeg = BuildSyntheticJpeg(width: 4, height: 3, components: 3);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(jpeg, 50, 50, 100, 75);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DCTDecode");
        asText.Should().Contain("/DeviceRGB");
        asText.Should().Contain("/Width 4");
        asText.Should().Contain("/Height 3");
    }

    [Fact]
    public void DrawImage_JpegGrayscale_PassesThroughAsDeviceGray()
    {
        byte[] jpeg = BuildSyntheticJpeg(width: 2, height: 2, components: 1);

        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(jpeg, 50, 50, 50, 50);
        byte[] bytes = doc.ToByteArray();

        string asText = Encoding.Latin1.GetString(bytes);
        asText.Should().Contain("/DCTDecode");
        asText.Should().Contain("/DeviceGray");
    }

    [Fact]
    public void DrawImage_UnknownFormat_Throws()
    {
        var doc = PdfDocumentBuilder.Create();
        doc.AddPage(PageSize.A4).DrawImage(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, 0, 10, 10);
        Action act = () => doc.ToByteArray();
        act.Should().Throw<ArgumentException>();
    }

    // ── ImagePdfConverter ─────────────────────────────────────────────────

    [Fact]
    public void Convert_SizeToImage_PageMatchesImageAtDpi()
    {
        // 200x100 px at 100 DPI → 144 x 72 points.
        byte[] png = EncodePng(SolidFrame(200, 100), includeAlpha: false);

        byte[] pdf = ImagePdfConverter.Convert(png, new ImagePdfOptions
        {
            Sizing = ImagePageSizing.SizeToImage,
            Dpi = 100,
        });

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
        read.Pages[0].Width.Should().BeApproximately(144, 0.1);
        read.Pages[0].Height.Should().BeApproximately(72, 0.1);
    }

    [Fact]
    public void Convert_FitToPage_UsesRequestedPaperSize()
    {
        byte[] png = EncodePng(SolidFrame(4000, 2000), includeAlpha: false);

        byte[] pdf = ImagePdfConverter.Convert(png, new ImagePdfOptions
        {
            Sizing = ImagePageSizing.FitToPage,
            PageSize = PageSize.A4,
        });

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.Pages[0].Width.Should().BeApproximately(595, 0.1);
        read.Pages[0].Height.Should().BeApproximately(842, 0.1);
    }

    [Fact]
    public void Convert_MultipleImages_OnePagePerImage()
    {
        byte[] a = EncodePng(SolidFrame(10, 10), includeAlpha: false);
        byte[] b = EncodePng(SolidFrame(20, 20), includeAlpha: false);
        byte[] c = EncodePng(SolidFrame(30, 30), includeAlpha: false);

        byte[] pdf = ImagePdfConverter.Convert(new List<byte[]> { a, b, c });

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(3);
    }

    [Fact]
    public void Convert_MultiFrameTiff_ExpandsToPagePerFrame()
    {
        byte[] tiff = TiffEncoder.EncodeAll(new[]
        {
            SolidFrame(8, 8),
            SolidFrame(8, 8),
            SolidFrame(8, 8),
        });

        byte[] pdf = ImagePdfConverter.Convert(tiff);

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(3);
    }

    [Fact]
    public void Convert_MultiFrameTiff_FirstFrameOnly_WhenExpandDisabled()
    {
        byte[] tiff = TiffEncoder.EncodeAll(new[] { SolidFrame(8, 8), SolidFrame(8, 8) });

        byte[] pdf = ImagePdfConverter.Convert(tiff, new ImagePdfOptions
        {
            ExpandTiffFrames = false,
        });

        using PdfDocument read = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        read.PageCount.Should().Be(1);
    }

    [Fact]
    public void Convert_SetsDocumentMetadata()
    {
        byte[] png = EncodePng(SolidFrame(5, 5), includeAlpha: false);

        byte[] pdf = ImagePdfConverter.Convert(png, new ImagePdfOptions
        {
            Title = "Scan 42",
            Author = "Chuvadi",
        });

        string asText = Encoding.Latin1.GetString(pdf);
        // PdfWriter serialises strings in hex form: "Scan 42" / "Chuvadi".
        asText.Should().Contain("/Title <5363616E203432>");
        asText.Should().Contain("/Author <43687576616469>");
    }

    [Fact]
    public void Convert_EmptyImageList_Throws()
    {
        Action act = () => ImagePdfConverter.Convert(new List<byte[]>());
        act.Should().Throw<ArgumentException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ImageFrame SolidFrame(int width, int height)
    {
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, 80, 120, 160, 255);
            }
        }
        return frame;
    }

    private static byte[] EncodePng(ImageFrame frame, bool includeAlpha)
    {
        using MemoryStream ms = new();
        PngEncoder.Encode(frame, ms, includeAlpha);
        return ms.ToArray();
    }

    // A structurally valid JPEG header (SOI + SOF0 + EOI). The embedder only
    // parses the SOF for dimensions and component count; the passthrough never
    // decodes scan data.
    private static byte[] BuildSyntheticJpeg(int width, int height, int components)
    {
        List<byte> b = new();
        b.AddRange(new byte[] { 0xFF, 0xD8 });           // SOI
        b.AddRange(new byte[] { 0xFF, 0xC0 });           // SOF0
        int len = 8 + (components * 3);
        b.Add((byte)(len >> 8));
        b.Add((byte)(len & 0xFF));
        b.Add(8);                                         // precision
        b.Add((byte)(height >> 8));
        b.Add((byte)(height & 0xFF));
        b.Add((byte)(width >> 8));
        b.Add((byte)(width & 0xFF));
        b.Add((byte)components);
        for (int i = 0; i < components; i++)
        {
            b.Add((byte)(i + 1));                         // component id
            b.Add(0x11);                                  // sampling factors
            b.Add(0);                                     // quant table
        }
        b.AddRange(new byte[] { 0xFF, 0xD9 });            // EOI
        return b.ToArray();
    }
}
