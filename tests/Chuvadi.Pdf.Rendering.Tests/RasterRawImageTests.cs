// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images (raw sample data, /Decode,
//        /ColorSpace), §7.4.6 — CCITTFaxDecode
// PHASE: Phase 2.9 — Reader feature batch (raster raw-image support) tests
//
// Before this phase the raster display-list builder only rendered images
// whose decoded bytes were a self-describing JPEG; raw sample images
// (FlateDecode RGB/Gray, CCITTFaxDecode bilevel) were silently dropped.
// These tests feed image XObjects straight through the content-bytes Build
// overload and assert the produced DrawImageOp pixels.

using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterRawImageTests
{
    private static readonly string CcittFixtureRoot =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Ccitt");

    // ── Helpers ───────────────────────────────────────────────────────────

    private static PdfDictionary ImageDict(int width, int height, int bpc, string colorSpace)
    {
        PdfDictionary dict = new();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("XObject"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        dict.Set(PdfName.Intern("Width"), width);
        dict.Set(PdfName.Intern("Height"), height);
        dict.Set(PdfName.Intern("BitsPerComponent"), bpc);
        dict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern(colorSpace));
        return dict;
    }

    private static PageDisplayList BuildWithImage(PdfStream image)
    {
        PdfDictionary xobjects = new();
        xobjects.Set(PdfName.Intern("Im1"), image);
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("XObject"), xobjects);

        byte[] content = Encoding.ASCII.GetBytes("q 100 0 0 100 0 0 cm /Im1 Do Q");
        PdfObjectStore store = new();
        return DisplayListBuilder.Build(content, resources, store, 612, 792);
    }

    private static DrawImageOp SingleImageOp(PageDisplayList list)
    {
        DrawImageOp[] images = list.Ops.OfType<DrawImageOp>().ToArray();
        images.Should().HaveCount(1);
        return images[0];
    }

    // ── Raw sample images ─────────────────────────────────────────────────

    [Fact]
    public void RawRgb24_RendersWithCorrectPixels()
    {
        // 2×2: red, green / blue, white — unfiltered raw samples.
        byte[] samples =
        [
            255, 0, 0,   0, 255, 0,
            0, 0, 255,   255, 255, 255,
        ];
        PdfStream image = new(ImageDict(2, 2, 8, "DeviceRGB"), samples);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Width.Should().Be(2);
        op.Image.Height.Should().Be(2);
        op.Image.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
        op.Image.Pixels.GetPixelBgra(1, 0).Should().Be(((byte)0, (byte)255, (byte)0, (byte)255));
        op.Image.Pixels.GetPixelBgra(0, 1).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        op.Image.Pixels.GetPixelBgra(1, 1).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void RawGray8_RendersWithCorrectPixels()
    {
        byte[] samples = [0, 128, 255, 64];
        PdfStream image = new(ImageDict(2, 2, 8, "DeviceGray"), samples);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)0, (byte)0, (byte)0, (byte)255));
        op.Image.Pixels.GetPixelBgra(1, 0).Should().Be(((byte)128, (byte)128, (byte)128, (byte)255));
        op.Image.Pixels.GetPixelBgra(0, 1).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void RawGray8_DecodeArray_Inverts()
    {
        byte[] samples = [0, 255];
        PdfDictionary dict = ImageDict(2, 1, 8, "DeviceGray");
        PdfArray decode = new();
        decode.Add(new PdfInteger(1));
        decode.Add(new PdfInteger(0));
        dict.Set(PdfName.Intern("Decode"), decode);
        PdfStream image = new(dict, samples);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
        op.Image.Pixels.GetPixelBgra(1, 0).Should().Be(((byte)0, (byte)0, (byte)0, (byte)255));
    }

    [Fact]
    public void RawBilevel_OneBitPerComponent_Renders()
    {
        // 8×2, one byte per row: top row 10110000, bottom row 00001101.
        // PDF default: 1 bits are white, 0 bits black.
        byte[] samples = [0b10110000, 0b00001101];
        PdfStream image = new(ImageDict(8, 2, 1, "DeviceGray"), samples);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Pixels.GetPixelBgra(0, 0).B.Should().Be(255);
        op.Image.Pixels.GetPixelBgra(1, 0).B.Should().Be(0);
        op.Image.Pixels.GetPixelBgra(2, 0).B.Should().Be(255);
        op.Image.Pixels.GetPixelBgra(7, 1).B.Should().Be(255);
        op.Image.Pixels.GetPixelBgra(6, 1).B.Should().Be(0);
    }

    [Fact]
    public void CcittGroup4Image_DecodesAndRenders()
    {
        // The bar_64x16 G4 fixture (independent Pillow reference). The strip
        // carries the inverted image (see CcittFaxFilterTests); rows 0–3
        // decode to fax-black and the 24-pixel bar region to fax-white.
        byte[] strip = File.ReadAllBytes(Path.Combine(CcittFixtureRoot, "bar_64x16_group4.bin"));

        PdfDictionary dict = ImageDict(64, 16, 1, "DeviceGray");
        dict.Set(PdfName.Intern("Filter"), PdfName.Intern("CCITTFaxDecode"));
        PdfDictionary parms = new();
        parms.Set(PdfName.Intern("K"), -1);
        parms.Set(PdfName.Intern("Columns"), 64);
        parms.Set(PdfName.Intern("Rows"), 16);
        dict.Set(PdfName.Intern("DecodeParms"), parms);
        PdfStream image = new(dict, strip);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Width.Should().Be(64);
        op.Image.Height.Should().Be(16);
        op.Image.Pixels.GetPixelBgra(0, 0).B.Should().Be(0);     // fax-black row
        op.Image.Pixels.GetPixelBgra(25, 5).B.Should().Be(255);  // bar region
        op.Image.Pixels.GetPixelBgra(5, 5).B.Should().Be(0);
    }

    [Fact]
    public void IccBasedThreeComponents_TreatedAsRgb()
    {
        byte[] samples = [10, 20, 30];
        PdfDictionary dict = ImageDict(1, 1, 8, "DeviceRGB");

        PdfDictionary iccDict = new();
        iccDict.Set(PdfName.Intern("N"), 3);
        PdfStream iccStream = new(iccDict, []);
        PdfArray colorSpace = new();
        colorSpace.Add(PdfName.Intern("ICCBased"));
        colorSpace.Add(iccStream);
        dict.Set(PdfName.Intern("ColorSpace"), colorSpace);
        PdfStream image = new(dict, samples);

        DrawImageOp op = SingleImageOp(BuildWithImage(image));

        op.Image.Pixels.GetPixelBgra(0, 0).Should().Be(((byte)30, (byte)20, (byte)10, (byte)255));
    }

    [Fact]
    public void ImageMask_IsStillSkipped()
    {
        // Stencil masks need fill-colour compositing the rasterizer doesn't
        // do yet (recorded follow-up): they must not emit a frame.
        byte[] samples = [0xF0];
        PdfDictionary dict = ImageDict(8, 1, 1, "DeviceGray");
        dict.Set(PdfName.Intern("ImageMask"), true);
        PdfStream image = new(dict, samples);

        PageDisplayList list = BuildWithImage(image);

        list.Ops.OfType<DrawImageOp>().Should().BeEmpty();
    }

    [Fact]
    public void UnsupportedColorSpace_IsSkippedNotThrown()
    {
        byte[] samples = [1, 2, 3, 4];
        PdfStream image = new(ImageDict(1, 1, 8, "DeviceCMYK"), samples);

        PageDisplayList list = BuildWithImage(image);

        list.Ops.OfType<DrawImageOp>().Should().BeEmpty();
    }
}
