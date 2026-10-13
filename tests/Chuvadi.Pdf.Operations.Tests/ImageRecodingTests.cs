// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.5 (image XObjects), §8.6.6.3 (Indexed spaces)
// PHASE: 3.18 — compression-path image recoding: downsample-to-DPI and
//        smart per-image codec selection.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ImageRecodingTests
{
    // A 400×400 noise image placed 72×72 points (1 inch) — 400 effective DPI.
    private static byte[] BuildPdfWithNoiseImage()
    {
        ImageFrame frame = ImageFrame.Create(400, 400, ImageColorFormat.Rgb24);
        uint seed = 42;
        for (int y = 0; y < 400; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                seed = (seed * 1664525u) + 1013904223u;
                frame.Pixels.SetPixelBgra(
                    x, y, (byte)(seed & 0xFF), (byte)((seed >> 8) & 0xFF),
                    (byte)((seed >> 16) & 0xFF), 255);
            }
        }

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawImage(frame, 50, 50, 72, 72);
        return builder.ToByteArray();
    }

    // A 256×256 two-colour checkerboard — the palette case.
    private static byte[] BuildPdfWithTwoColourImage()
    {
        ImageFrame frame = ImageFrame.Create(256, 256, ImageColorFormat.Rgb24);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                bool red = ((x / 8) + (y / 8)) % 2 == 0;
                frame.Pixels.SetPixelBgra(
                    x, y, (byte)(red ? 0 : 255), 0, (byte)(red ? 255 : 0), 255);
            }
        }

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawImage(frame, 50, 50, 200, 200);
        return builder.ToByteArray();
    }

    private static byte[] Compress(byte[] source, CompressionOptions options)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(source), leaveOpen: true);
        using MemoryStream output = new MemoryStream();
        PdfCompressor.Compress(document, output, options);
        return output.ToArray();
    }

    private static PdfStream FirstImage(byte[] pdf)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: true);
        PdfDictionary? resources = document.Pages[0].Resources;
        resources.Should().NotBeNull();
        resources!.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv).Should().BeTrue();
        PdfDictionary xobjects = (PdfDictionary)document.Objects.Resolve(xv!);
        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in xobjects)
        {
            if (document.Objects.Resolve(entry.Value) is PdfStream stream)
            {
                return stream;
            }
        }

        throw new Xunit.Sdk.XunitException("no image XObject found");
    }

    [Fact]
    public void Downsample_HighDpiImage_IsResampledToTarget()
    {
        byte[] source = BuildPdfWithNoiseImage();
        byte[] compressed = Compress(source, new CompressionOptions
        {
            RecompressImages = true,
            DownsampleImagesToDpi = 100,
        });

        PdfStream image = FirstImage(compressed);
        int width = ((PdfInteger)image.Dictionary.GetAs<PdfPrimitive>(
            PdfName.Intern("Width"))!).Value;

        // 72 points at 100 DPI = 100 pixels (was 400).
        width.Should().Be(100, "a 400-DPI placement downsamples to the 100-DPI target");
        compressed.Length.Should().BeLessThan(source.Length);
    }

    [Fact]
    public void Downsample_Disabled_KeepsOriginalResolution()
    {
        byte[] source = BuildPdfWithNoiseImage();
        byte[] compressed = Compress(source, new CompressionOptions
        {
            RecompressImages = true,
        });

        PdfStream image = FirstImage(compressed);
        int width = ((PdfInteger)image.Dictionary.GetAs<PdfPrimitive>(
            PdfName.Intern("Width"))!).Value;

        width.Should().Be(400, "downsampling is opt-in");
    }

    [Fact]
    public void CodecSelection_TwoColourImage_StoresAnIndexedPalette()
    {
        byte[] source = BuildPdfWithTwoColourImage();
        byte[] compressed = Compress(source, new CompressionOptions
        {
            RecompressImages = true,
            SmartCodecSelection = true,
        });

        PdfStream image = FirstImage(compressed);
        PdfDictionary dict = image.Dictionary;

        ((PdfName)dict.GetAs<PdfPrimitive>(PdfName.Intern("Filter"))!).Value
            .Should().Be("FlateDecode", "the palette candidate wins for a two-colour image");
        dict.GetAs<PdfPrimitive>(PdfName.Intern("ColorSpace"))
            .Should().BeOfType<PdfArray>("the colour space is [/Indexed /DeviceRGB hival palette]");
        ((PdfInteger)dict.GetAs<PdfPrimitive>(PdfName.Intern("BitsPerComponent"))!).Value
            .Should().Be(1, "two palette entries need one bit per index");
    }

    [Fact]
    public void CodecSelection_BeatsJpegOnlyForFlatArt()
    {
        byte[] source = BuildPdfWithTwoColourImage();

        byte[] jpegOnly = Compress(source, new CompressionOptions
        {
            RecompressImages = true,
        });
        byte[] selected = Compress(source, new CompressionOptions
        {
            RecompressImages = true,
            SmartCodecSelection = true,
        });

        selected.Length.Should().BeLessThan(jpegOnly.Length,
            "a 1-bpc indexed checkerboard under Flate is far smaller than its JPEG");
    }
}
