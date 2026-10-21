// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.22: a redaction box over part of an image blanks only the
// covered pixels (the rest of the image is kept), instead of removing the
// whole image.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionPartialImageTests
{
    private const string Media = "/MediaBox[0 0 400 400]";

    // The image is 10x10 pixels painted over (100,100)-(200,200): one pixel
    // per 10 points. The box covers its left half (x 100..150).
    private static readonly RectangleF LeftHalf = new RectangleF(90, 50, 60, 300);

    [Fact]
    public void PartlyCoveredImage_BlanksOnlyTheCoveredPixels()
    {
        byte[] output = Redact(
            PageWith("q 100 0 0 100 100 100 cm /Im0 Do Q", "/XObject<</Im0 5 0 R>>", MarkerImage()),
            LeftHalf);

        Latin1(output).Should().NotContain("QZX", "the covered pixels' bytes must leave the file");
        Latin1(output).Should().NotContain("/Im0 Do");

        using PdfDocument doc = Open(output);
        (PdfStream image, byte[] samples) = FirstImage(doc, 0);
        IntOf(doc, image, "Width").Should().Be(10);
        IntOf(doc, image, "Height").Should().Be(10);
        for (int row = 0; row < 10; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                int p = ((row * 10) + col) * 3;
                if (col < 5)
                {
                    samples[p].Should().Be(0);
                    samples[p + 1].Should().Be(0);
                    samples[p + 2].Should().Be(0);
                }
                else
                {
                    Encoding.Latin1.GetString(samples, p, 3).Should().Be("KEP");
                }
            }
        }
    }

    [Fact]
    public void PartlyCoveredImage_VerifiesClean()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            MarkerImage(),
        });
        List<RedactionRect> rects = new List<RedactionRect> { new RedactionRect(0, LeftHalf) };

        using (PdfDocument original = Open(input))
        {
            RedactionVerifier.Verify(original, rects).Survivors
                .Should().Contain(s => s.Kind == RedactionSurvivorKind.Image);
        }

        byte[] output = Redact(input, LeftHalf);
        using PdfDocument redacted = Open(output);
        RedactionVerifier.Verify(redacted, rects).IsClean.Should().BeTrue();

        // The kept half still paints: a box over it finds the image.
        RedactionVerifier.ExtractWithin(redacted, 0, new RectangleF(160, 120, 20, 20))
            .Should().Contain(s => s.Kind == RedactionSurvivorKind.Image);
    }

    [Fact]
    public void RotatedImage_BlanksThePixelsUnderTheBox()
    {
        // Rotated 90 degrees: (u, v) maps to (200 - 100v, 100 + 100u), so the
        // box over x 150..200 covers v 0..0.5, the bottom five pixel rows.
        byte[] output = Redact(
            PageWith("q 0 100 -100 0 200 100 cm /Im0 Do Q", "/XObject<</Im0 5 0 R>>", MarkerImage()),
            new RectangleF(150, 50, 60, 300));

        using PdfDocument doc = Open(output);
        (PdfStream _, byte[] samples) = FirstImage(doc, 0);
        for (int row = 0; row < 10; row++)
        {
            int p = ((row * 10) + 7) * 3;
            if (row >= 5)
            {
                samples[p].Should().Be(0, "row {0} lies under the box", row);
            }
            else
            {
                samples[p].Should().Be((byte)'K', "row {0} lies outside the box", row);
            }
        }
    }

    [Fact]
    public void SoftMask_IsBlankedOverTheSameRegion()
    {
        byte[] alpha = new byte[10 * 10];
        System.Array.Fill(alpha, (byte)255);
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            MarkerImage("/SMask 6 0 R"),
            Stream("/Type/XObject/Subtype/Image/Width 10/Height 10/ColorSpace/DeviceGray/BitsPerComponent 8", alpha),
        });

        using PdfDocument doc = Open(Redact(input, LeftHalf));
        (PdfStream image, byte[] _) = FirstImage(doc, 0);
        PdfStream mask = (PdfStream)doc.Objects.Resolve(image.Dictionary[PdfName.Intern("SMask")]);
        byte[] maskSamples = Inflate(mask.RawBytes);
        maskSamples[0].Should().Be(0, "the covered pixel is made transparent");
        maskSamples[9].Should().Be(255, "the kept pixel stays opaque");
    }

    [Fact]
    public void InlineImage_IsBlankedIntoAnImageXObject()
    {
        StringBuilder data = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            data.Append((i % 10) < 5 ? 'Q' : 'K');
        }

        byte[] output = Redact(
            PageWith("q 100 0 0 100 100 100 cm BI /W 10 /H 10 /BPC 8 /CS /G ID\n" + data + "\nEI Q", string.Empty),
            LeftHalf);

        string text = Latin1(output);
        text.Should().NotContain("QQQQQ", "the covered inline-image samples must leave the file");
        text.Should().Contain("/CvRedIm0 Do");

        using PdfDocument doc = Open(output);
        (PdfStream _, byte[] samples) = FirstImage(doc, 0);
        samples[0].Should().Be(0);
        samples[9].Should().Be((byte)'K');
    }

    [Fact]
    public void ImageInAFormWithItsOwnResources_IsBlanked()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Fm0 5 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "/Fm0 Do"),
                Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</XObject<</Im0 6 0 R>>>>",
                    "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
            }),
            LeftHalf);

        Latin1(output).Should().NotContain("QZX");
        using PdfDocument doc = Open(output);
        PdfDictionary pageXObjects = (PdfDictionary)doc.Objects.Resolve(doc.Pages[0].Resources![PdfName.XObject]);
        PdfStream form = (PdfStream)doc.Objects.Resolve(pageXObjects[PdfName.Intern("Fm0")]);
        PdfDictionary formResources = (PdfDictionary)doc.Objects.Resolve(form.Dictionary[PdfName.Resources]);
        (PdfStream _, byte[] samples) = FirstImage(doc, formResources);
        samples[0].Should().Be(0, "a covered pixel");
        Encoding.Latin1.GetString(samples, 27, 3).Should().Be("KEP", "a kept pixel");
    }

    [Fact]
    public void ImageInAResourceInheritingForm_IsRemoved()
    {
        // The form draws the page's /Im0 without resources of its own, so no
        // new image name can be added to it: the image is removed whole.
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media
                    + "/Resources<</XObject<</Fm0 5 0 R/Im0 6 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "/Fm0 Do"),
                Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
            }),
            LeftHalf);

        Latin1(output).Should().NotContain("QZX");
        Latin1(output).Should().NotContain("/Im0 Do");
    }

    [Fact]
    public void ImageSharedWithAnUnredactedPage_KeepsTheOriginalThere()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R 5 0 R]/Count 2>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 6 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
            }),
            LeftHalf);

        Latin1(output).Should().Contain("QZX", "page 2 still paints the original image");
        using PdfDocument doc = Open(output);
        (PdfStream _, byte[] samples) = FirstImage(doc, 0);
        samples[0].Should().Be(0, "page 1 paints the blanked copy");
    }

    [Fact]
    public void StencilMask_CoveredSamplesPaintNothing()
    {
        // 16x2 stencil, all samples 0 (paint). Blanked samples become 1.
        byte[] bits = new byte[2 * 2];
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "q 0 g 160 0 0 100 100 100 cm /Im0 Do Q"),
                Stream("/Type/XObject/Subtype/Image/Width 16/Height 2/ImageMask true/BitsPerComponent 1", bits),
            }),
            LeftHalf);

        using PdfDocument doc = Open(output);
        (PdfStream _, byte[] samples) = FirstImage(doc, 0);

        // The box reaches x 150: pixel columns 0..4 (each 10 pt wide) are covered.
        samples[0].Should().Be(0b1111_1000);
        samples[1].Should().Be(0);
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, LeftHalf) })
            .IsClean.Should().BeTrue("the covered stencil samples paint nothing");
    }

    [Fact]
    public void JpegImage_IsBlankedAndReEncoded()
    {
        PixelBuffer pixels = new PixelBuffer(16, 16);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                pixels.SetPixelBgra(x, y, 60, 120, 200, 255);
            }
        }

        using MemoryStream jpeg = new MemoryStream();
        JpegEncoder.Encode(new ImageFrame(pixels, ImageColorFormat.Rgb24), jpeg, 95);
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "q 160 0 0 160 100 100 cm /Im0 Do Q"),
                Stream("/Type/XObject/Subtype/Image/Width 16/Height 16/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode",
                    jpeg.ToArray()),
            }),
            LeftHalf);

        using PdfDocument doc = Open(output);
        (PdfStream image, byte[] samples) = FirstImage(doc, 0);
        image.Dictionary.TryGetValue(PdfName.Filter, out PdfPrimitive? filter).Should().BeTrue();
        ((PdfName)filter!).Value.Should().Be("FlateDecode");
        samples[0].Should().Be(0, "a covered pixel");
        samples[(15 * 3)].Should().BeGreaterThan(150, "a kept pixel keeps its red");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageLabelledByTheStructureTree_StillLeavesTheFile(bool wholePage)
    {
        // A tagged Figure points at the image through an object reference
        // (OBJR); the label must not keep the removed pixels in the file.
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R/StructTreeRoot 6 0 R/MarkInfo<</Marked true>>>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/StructParents 0/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "/Figure<</MCID 0>>BDC q 100 0 0 100 100 100 cm /Im0 Do Q EMC"),
                MarkerImage(),
                Obj("<</Type/StructTreeRoot/K 7 0 R>>"),
                Obj("<</Type/StructElem/S/Figure/P 6 0 R/Pg 3 0 R/K[0 <</Type/OBJR/Obj 5 0 R/Pg 3 0 R>>]>>"),
            }),
            wholePage ? new RectangleF(0, 0, 400, 400) : LeftHalf);

        Latin1(output).Should().NotContain("QZX");
        using PdfDocument doc = Open(output);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void ReRedactingAFileWithABlankedImage_GivesEachPlacementItsOwnCopy()
    {
        // The page already uses /CvRedIm0 (an earlier redaction); two
        // placements of /Im0 are partly covered by different boxes.
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R/CvRedIm0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q q 100 0 0 100 100 250 cm /Im0 Do Q q 1 0 0 1 380 380 cm /CvRedIm0 Do Q"),
            MarkerImage(),
            MarkerImage(),
        });
        List<RedactionRect> rects = new List<RedactionRect>
        {
            new RedactionRect(0, new RectangleF(90, 90, 60, 120)),
            new RedactionRect(0, new RectangleF(150, 240, 60, 120)),
        };

        using PdfDocument doc = Open(input);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions { Rectangles = rects });

        using PdfDocument redacted = Open(output.ToArray());
        RedactionVerifier.Verify(redacted, rects).IsClean.Should().BeTrue();
    }

    [Fact]
    public void ImageInResourcesInheritedFromThePageTree_LeavesTheFile()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1/Resources<</XObject<</Im0 5 0 R>>>>>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
            }),
            LeftHalf);

        Latin1(output).Should().NotContain("QZX", "the page-tree node must not keep the original image");
        using PdfDocument doc = Open(output);
        (PdfStream _, byte[] samples) = FirstImage(doc, 0);
        Encoding.Latin1.GetString(samples, 27, 3).Should().Be("KEP");
    }

    [Fact]
    public void ImageInheritedByAnotherPage_IsKeptForIt()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R 6 0 R]/Count 2/Resources<</XObject<</Im0 5 0 R>>>>>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 7 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            }),
            LeftHalf);

        Latin1(output).Should().Contain("QZX", "page 2 still draws the inherited image");
    }

    [Fact]
    public void UndecodableSiblingPage_DoesNotKeepTheInheritedImage()
    {
        // Page 2 inherits the same resources but its content cannot be
        // decoded, so it draws nothing in any viewer.
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R 6 0 R]/Count 2/Resources<</XObject<</Im0 5 0 R>>>>>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 7 0 R>>"),
                Stream("/Filter/FlateDecode", "not flate data"),
            }),
            LeftHalf);

        Latin1(output).Should().NotContain("QZX");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedResourceDictionary_DoesNotKeepTheImage(bool throughNodes)
    {
        // One indirect resource dictionary (object 10) used by the redacted
        // page and by another page that never draws /Im0 — through two
        // page-tree nodes, or directly by both pages.
        List<byte[]> bodies = throughNodes
            ? new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[8 0 R 9 0 R]/Count 2>>"),
                Obj("<</Type/Page/Parent 8 0 R" + Media + "/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
                Obj("<</Type/Page/Parent 9 0 R" + Media + "/Contents 7 0 R>>"),
                Stream(string.Empty, "0 g 1 1 2 2 re f"),
                Obj("<</Type/Pages/Parent 2 0 R/Kids[3 0 R]/Count 1/Resources 10 0 R>>"),
                Obj("<</Type/Pages/Parent 2 0 R/Kids[6 0 R]/Count 1/Resources 10 0 R>>"),
                Obj("<</XObject<</Im0 5 0 R>>>>"),
            }
            : new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R 6 0 R]/Count 2>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources 8 0 R/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources 8 0 R/Contents 7 0 R>>"),
                Stream(string.Empty, "0 g 1 1 2 2 re f"),
                Obj("<</XObject<</Im0 5 0 R>>>>"),
            };

        byte[] output = Redact(Build(bodies), LeftHalf);

        Latin1(output).Should().NotContain("QZX");
        using PdfDocument doc = Open(output);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void ResourceDictionarySharedWithAForm_IsLeftWhole()
    {
        // Object 8 is the page's resources AND the form's: the form draws
        // /Im1 through it, so pruning by page content would break the form.
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R 6 0 R]/Count 2>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources 8 0 R/Contents 4 0 R>>"),
                Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
                MarkerImage(),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources 8 0 R/Contents 7 0 R>>"),
                Stream(string.Empty, "/Fm0 Do"),
                Obj("<</XObject<</Im0 5 0 R/Fm0 9 0 R/Im1 10 0 R>>>>"),
                Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources 8 0 R", "q 10 0 0 10 300 300 cm /Im1 Do Q"),
                Stream("/Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8", "FRM"),
            }),
            LeftHalf);

        Latin1(output).Should().Contain("FRM", "the form still draws its image");
    }

    [Fact]
    public void RedactedForm_DropsItsPieceInfo()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Fm0 5 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "/Fm0 Do"),
                Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/PieceInfo<</App<</Private 6 0 R>>>>",
                    "0 0 1 rg 100 100 50 50 re f"),
                Stream(string.Empty, "PRIVATEFORMDATA"),
            }),
            LeftHalf);

        Latin1(output).Should().NotContain("PRIVATEFORMDATA");
    }

    [Fact]
    public void Verifier_ReportsPixelsHiddenUnderATransparentSoftMask()
    {
        // Nothing redacted: the soft mask is fully transparent, but the
        // colour samples under it still hold the picture.
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            MarkerImage("/SMask 6 0 R"),
            Stream("/Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", new byte[] { 0 }),
        });

        using PdfDocument doc = Open(input);
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, LeftHalf) }).Survivors
            .Should().Contain(s => s.Kind == RedactionSurvivorKind.Image);
    }

    [Fact]
    public void Verifier_ReportsWhenTheComponentCountIsAmbiguous()
    {
        // 2x1, 1 bit per component, four-channel ICC: one data byte fits
        // three or four components, so the pixel layout is unknown.
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            Stream("/Type/XObject/Subtype/Image/Width 2/Height 1/ColorSpace[/ICCBased 6 0 R]/BitsPerComponent 1", new byte[] { 0x03 }),
            Stream("/N 4", "ICC"),
        });

        using PdfDocument doc = Open(input);
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, new RectangleF(160, 120, 30, 50)) })
            .Survivors.Should().Contain(s => s.Kind == RedactionSurvivorKind.Image);
    }

    [Fact]
    public void Verifier_ReadsFourChannelIccImagesAtTheirRealWidth()
    {
        // 4x1 ICC (N 4) image: pixels 0-2 zero, pixel 3 "QZXK"; the box
        // covers only pixel 3.
        byte[] data = new byte[16];
        data[12] = (byte)'Q';
        data[13] = (byte)'Z';
        data[14] = (byte)'X';
        data[15] = (byte)'K';
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            Stream("/Type/XObject/Subtype/Image/Width 4/Height 1/ColorSpace[/ICCBased 6 0 R]/BitsPerComponent 8", data),
            Stream("/N 4", "ICC"),
        });

        using PdfDocument doc = Open(input);
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, new RectangleF(180, 120, 15, 50)) })
            .Survivors.Should().Contain(s => s.Kind == RedactionSurvivorKind.Image);
    }

    [Fact]
    public void RedactedPage_DropsItsThumbnailAndPieceInfo()
    {
        byte[] output = Redact(
            Build(new List<byte[]>
            {
                Obj("<</Type/Catalog/Pages 2 0 R>>"),
                Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
                Obj("<</Type/Page/Parent 2 0 R" + Media
                    + "/Thumb 5 0 R/PieceInfo<</Illustrator<</Private 6 0 R>>>>/Contents 4 0 R>>"),
                Stream(string.Empty, "0 0 1 rg 100 100 50 50 re f"),
                Stream("/Width 2/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8", "THUMBX"),
                Stream(string.Empty, "PRIVATEAPPDATA"),
            }),
            LeftHalf);

        string text = Latin1(output);
        text.Should().NotContain("THUMBX", "a thumbnail pictures the unredacted page");
        text.Should().NotContain("PRIVATEAPPDATA");
    }

    // One page (objects 1-4) followed by extra objects numbered from 5.
    private static byte[] PageWith(string content, string resources, params byte[][] extra)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<<" + resources + ">>/Contents 4 0 R>>"),
            Stream(string.Empty, content),
        };
        bodies.AddRange(extra);
        return Build(bodies);
    }

    private static byte[] Redact(byte[] input, RectangleF box)
    {
        using PdfDocument doc = Open(input);
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, box) },
        });

        return output.ToArray();
    }

    private static (PdfStream Image, byte[] Samples) FirstImage(PdfDocument doc, int pageIndex)
    {
        return FirstImage(doc, doc.Pages[pageIndex].Resources!);
    }

    private static (PdfStream Image, byte[] Samples) FirstImage(PdfDocument doc, PdfDictionary resources)
    {
        PdfDictionary xobjects = (PdfDictionary)doc.Objects.Resolve(resources[PdfName.XObject]);
        foreach (PdfName key in xobjects.Keys)
        {
            if (doc.Objects.Resolve(xobjects[key]) is PdfStream stream
                && stream.Dictionary.TryGetValue(PdfName.Subtype, out PdfPrimitive? st)
                && st is PdfName subtype && subtype.Value == "Image")
            {
                return (stream, Inflate(stream.RawBytes));
            }
        }

        throw new InvalidDataException("No image on the page.");
    }

    private static int IntOf(PdfDocument doc, PdfStream stream, string key)
    {
        return (int)((PdfInteger)doc.Objects.Resolve(stream.Dictionary[PdfName.Intern(key)])).Value;
    }

    private static byte[] Inflate(byte[] data)
    {
        using MemoryStream input = new MemoryStream(data);
        using ZLibStream z = new ZLibStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    // A 10x10 RGB image: left half pixels "QZX", right half "KEP".
    private static byte[] MarkerImage(string extra = "")
    {
        byte[] data = new byte[10 * 10 * 3];
        for (int i = 0; i < 100; i++)
        {
            string marker = (i % 10) < 5 ? "QZX" : "KEP";
            data[i * 3] = (byte)marker[0];
            data[(i * 3) + 1] = (byte)marker[1];
            data[(i * 3) + 2] = (byte)marker[2];
        }

        return Stream("/Type/XObject/Subtype/Image/Width 10/Height 10/ColorSpace/DeviceRGB/BitsPerComponent 8" + extra, data);
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string entries, string content) => Stream(entries, Encoding.Latin1.GetBytes(content));

    private static byte[] Stream(string entries, byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<<" + entries + "/Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(content);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        return ms.ToArray();
    }
}
