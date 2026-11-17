// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images
// PHASE: 3.35.0 (D-391 I1) — ImageDecoding.Decode gives a display-list image's
//        pixels exactly as the raster route paints them. The probes are
//        Print's, measured on 3.34.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class ImageDecoding3350Tests
{
    // A 2 x 2 gray JPEG, every sample 40.
    private const string GrayJpegHex =
        "ffd8ffe000104a46494600010100000100010000ffdb00430001010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101ffc0000b080002000201011100ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffda0008010100003f00fe4febffd9";

    // A 2 x 1 CMYK JPEG with an Adobe marker, stored inverted: red, then white.
    private const string CmykJpegHex =
        "ffd8ffee000e41646f626500640000000000ffdb00430001010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101ffc000140800010002044311004d11005911004b1100ffc4001f0000010501010101010100000000000000000102030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffda000e0443004d0059004b00003f00fefe2bfaf9ff00824eff00ca2cbfe09a7ff6601fb1bffeb3afc39afebe7fe093bff28b2ff8269ffd9807ec6fff00acebf0e6bfbf8affd9";

    // I1: ICCBased /N 1 reads as gray.
    [Fact]
    public void IccBasedGray_DecodesAsGray()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace[/ICCBased 7 0 R]", Bytes(0, 255),
            "<</N 1/Length 4>>\nstream\nxxxx\nendstream");

        image.Pixels.Should().Equal(0, 0, 0, 255, 255, 255, 255, 255);
        image.HasAlpha.Should().BeFalse();
    }

    // I2: a soft mask on a JPEG gives the alpha.
    [Fact]
    public void JpegWithSoftMask_CarriesTheAlpha()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 2/BitsPerComponent 8/ColorSpace/DeviceGray/Filter/DCTDecode/SMask 7 0 R", Convert.FromHexString(GrayJpegHex),
            "<</Type/XObject/Subtype/Image/Width 2/Height 2/BitsPerComponent 8/ColorSpace/DeviceGray/Length 4>>\nstream\n" + Latin(Bytes(255, 128, 64, 0)) + "\nendstream");

        Alphas(image).Should().Equal(255, 128, 64, 0);
        image.HasAlpha.Should().BeTrue();
        image.CanPassThroughJpeg.Should().BeFalse("it has a soft mask");
        image.Pixels![0].Should().BeInRange(36, 44, "the gray samples are 40");
    }

    // I3: a colour-key /Mask hides the matching samples.
    [Fact]
    public void ColorKeyMask_HidesTheMatchingSamples()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace/DeviceRGB/Mask[255 255 0 0 0 0]", Bytes(255, 0, 0, 0, 0, 255));

        Alphas(image).Should().Equal(0, 255);
        image.HasAlpha.Should().BeTrue();
    }

    // I4: /Decode [1 0] inverts gray.
    [Fact]
    public void GrayDecodeArray_Inverts()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace/DeviceGray/Decode[1 0]", Bytes(0, 255));

        image.Pixels.Should().Equal(255, 255, 255, 255, 0, 0, 0, 255);
    }

    // I5: a 16-bit image is not drawn, and says why, as the raster route does.
    [Fact]
    public void SixteenBit_GivesNoPixelsAndTheReason()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 16/ColorSpace/DeviceGray", Bytes(0, 0, 255, 255), rasterWarnings: warnings);

        image.Pixels.Should().BeNull();
        image.Width.Should().Be(0);
        image.SkipReason.Should().NotBeNullOrEmpty();
        warnings.Select(w => w.Reason).Should().Contain(image.SkipReason);
    }

    // I6: a CMYK JPEG stored inverted, read through /Decode as in 3.34.0.
    [Fact]
    public void CmykJpegWithDecode_GivesRedThenWhite()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace/DeviceCMYK/Filter/DCTDecode/Decode[1 0 1 0 1 0 1 0]", Convert.FromHexString(CmykJpegHex));

        image.Pixels![0].Should().BeGreaterThan(200);
        image.Pixels[1].Should().BeLessThan(60);
        image.Pixels[2].Should().BeLessThan(60);
        image.Pixels.Skip(4).Take(3).Should().OnlyContain(v => v > 230);
        image.CanPassThroughJpeg.Should().BeFalse("it is four-component");
    }

    [Fact]
    public void PlainGrayJpeg_CanPassThrough()
    {
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 2/BitsPerComponent 8/ColorSpace/DeviceGray/Filter/DCTDecode", Convert.FromHexString(GrayJpegHex));

        image.CanPassThroughJpeg.Should().BeTrue();
        image.Width.Should().Be(2);
        image.HasAlpha.Should().BeFalse();
    }

    [Fact]
    public void StencilMask_TakesTheFillColour()
    {
        DecodedImage image = Decode("0 0 1 rg q 100 0 0 100 50 50 cm /Im Do Q", "<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 1/ImageMask true", Bytes(0x40), null, null);

        // Sample 0 paints (the default /Decode [0 1]), sample 1 does not.
        image.Pixels!.Take(4).Should().Equal(0, 0, 255, 255);
        image.Pixels![7].Should().Be(0);
        image.HasAlpha.Should().BeTrue();
    }

    [Fact]
    public void InlineImage_Decodes()
    {
        DecodedImage image = Decode("q 100 0 0 100 50 50 cm BI /W 2 /H 1 /BPC 8 /CS /G /D [1 0] ID " + Latin(Bytes(0, 255)) + " EI Q", null, null, null, null);

        image.Pixels.Should().Equal(255, 255, 255, 255, 0, 0, 0, 255);
    }

    // The decoded pixels are the ones the raster route paints: an
    // image scaled to cover the page and drawn over black matches them.
    [Fact]
    public void Pixels_MatchTheRasterRoute()
    {
        PixelBuffer px = Raster("q 200 0 0 200 0 0 cm /Im Do Q", "<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace[/ICCBased 7 0 R]", Bytes(30, 220),
            "<</N 1/Length 4>>\nstream\nxxxx\nendstream", null);
        DecodedImage image = DecodeOnly("<</Type/XObject/Subtype/Image/Width 2/Height 1/BitsPerComponent 8/ColorSpace[/ICCBased 7 0 R]", Bytes(30, 220),
            "<</N 1/Length 4>>\nstream\nxxxx\nendstream");

        px.GetPixelBgra(50, 100).R.Should().Be(image.Pixels![0]);
        px.GetPixelBgra(150, 100).R.Should().Be(image.Pixels[4]);
    }

    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    private static string Latin(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static List<byte> Alphas(DecodedImage image)
    {
        List<byte> alphas = new List<byte>();
        for (int i = 3; i < image.Pixels!.Length; i += 4)
        {
            alphas.Add(image.Pixels[i]);
        }

        return alphas;
    }

    private static DecodedImage DecodeOnly(string imageDict, byte[] data, string? object7 = null, List<PdfWarning>? rasterWarnings = null) =>
        Decode("q 100 0 0 100 50 50 cm /Im Do Q", imageDict, data, object7, rasterWarnings);

    private static DecodedImage Decode(string content, string? imageDict, byte[]? data, string? object7, List<PdfWarning>? rasterWarnings)
    {
        using PdfDocument doc = Open(content, imageDict, data, object7);
        if (rasterWarnings is not null)
        {
            doc.Objects.OnWarning = rasterWarnings.Add;
            new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
        }

        ImageOp op = DisplayListBuilder.Build(doc, 0).OfType<ImageOp>().Single();
        return ImageDecoding.Decode(op);
    }

    private static PixelBuffer Raster(string content, string imageDict, byte[] data, string? object7, List<PdfWarning>? warnings)
    {
        using PdfDocument doc = Open(content, imageDict, data, object7);
        if (warnings is not null)
        {
            doc.Objects.OnWarning = warnings.Add;
        }

        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }

    // Objects: 1 catalog, 2 pages, 3 page, 4 contents, 5 image (when given),
    // 6 unused, 7 the extra object.
    private static PdfDocument Open(string content, string? imageDict, byte[]? data, string? object7)
    {
        string image = imageDict is null
            ? "<<>>"
            : imageDict + "/Length " + data!.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + Latin(data) + "\nendstream";
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</XObject<</Im 5 0 R>>>>/Contents 4 0 R>>",
            "<</Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            image,
            "<<>>",
            object7 ?? "<<>>",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
    }
}
