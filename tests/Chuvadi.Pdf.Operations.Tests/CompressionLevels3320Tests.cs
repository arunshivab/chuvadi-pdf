// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-218 PDF-09 P4, D-243, D-268 to D-270) — named
//        compression levels, the three image levels, their options, and the
//        level walk in target mode.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class CompressionLevels3320Tests
{
    // ── Compress by level ───────────────────────────────────────────────

    [Fact]
    public void Lossless_LeavesImagesAlone()
    {
        CompressionResult result = Compress(PhotoPdf(), PdfCompressionLevel.Lossless, out byte[] bytes);

        result.Level.Should().Be(PdfCompressionLevel.Lossless);
        result.ImagesRecompressed.Should().Be(0);
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Medium_And_High_AreSmallerInTurn()
    {
        byte[] source = PhotoPdf();
        Compress(source, PdfCompressionLevel.Lossless, out byte[] lossless);
        CompressionResult medium = Compress(source, PdfCompressionLevel.Medium, out byte[] mediumBytes);
        CompressionResult high = Compress(source, PdfCompressionLevel.High, out byte[] highBytes);

        medium.ImagesRecompressed.Should().BeGreaterThan(0);
        high.ImagesRecompressed.Should().BeGreaterThan(0);
        mediumBytes.Length.Should().BeLessThan(lossless.Length);
        highBytes.Length.Should().BeLessThan(mediumBytes.Length);
    }

    [Fact]
    public void BlackAndWhite_WritesOneBilevelImagePerPage_AtTheShownSize()
    {
        CompressionResult result = Compress(DrawingPdf(rotate: 90), PdfCompressionLevel.BlackAndWhite, out byte[] bytes);

        result.Level.Should().Be(PdfCompressionLevel.BlackAndWhite);
        result.Skipped.Should().BeFalse();
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);
        doc.PageCount.Should().Be(1);
        doc.Pages[0].Rotate.Should().Be(0);
        doc.Pages[0].Width.Should().BeApproximately(300, 1, "the 400 x 300 page is shown rotated");
        doc.Pages[0].Height.Should().BeApproximately(400, 1);

        PdfDictionary xobjects = doc.Objects.ResolveAs<PdfDictionary>(
            doc.Objects.ResolveAs<PdfDictionary>(doc.Pages[0].Dictionary[PdfName.Resources])![PdfName.XObject])!;
        PdfStream image = (PdfStream)doc.Objects.Resolve(xobjects[PdfName.Intern("Im0")]);
        ((PdfInteger)image.Dictionary[PdfName.Intern("BitsPerComponent")]).Value.Should().Be(1);
        image.Dictionary.GetName(PdfName.Intern("ColorSpace")).Should().Be(PdfName.Intern("DeviceGray"));
    }

    [Fact]
    public void BlackAndWhite_KeepsThinLightLines_AsBlack()
    {
        Compress(DrawingPdf(rotate: 0), PdfCompressionLevel.BlackAndWhite, out byte[] bytes);
        PixelBuffer page = Render(bytes, 150);

        // The 1 pt light-grey (0.8) line at y = 250 pt from the bottom.
        int row = (int)((300 - 250) * 150 / 72.0);
        bool anyBlack = false;
        for (int dy = -2; dy <= 2; dy++)
        {
            anyBlack |= page.GetPixelBgra(150, row + dy).R < 64;
        }

        anyBlack.Should().BeTrue("a light line is ink and must survive");
    }

    [Fact]
    public void BlackAndWhite_Fills_DotPatternOrSolid()
    {
        byte[] source = DrawingPdf(rotate: 0);
        Compress(source, PdfCompressionLevel.BlackAndWhite, out byte[] dots);
        Compress(source, PdfCompressionLevel.BlackAndWhite, new ImageLevelOptions { Fills = BlackAndWhiteFills.Solid }, out byte[] solid);

        // The 0.6 grey square from (250, 50) to (350, 150).
        double dotsBlack = BlackShare(Render(dots, 150), 260, 60, 340, 140);
        double solidBlack = BlackShare(Render(solid, 150), 260, 60, 340, 140);

        solidBlack.Should().BeGreaterThan(0.99, "Solid paints the fill black");
        dotsBlack.Should().BeInRange(0.2, 0.8, "DotPattern keeps it a pattern");
        solid.Length.Should().BeLessThan(dots.Length);
    }

    [Fact]
    public void BlackAndWhite_SkipsASignedDocument_LikeEveryLevel()
    {
        CompressionResult result = Compress(PdfCompressorGuardTests.BuildSignedPdf(), PdfCompressionLevel.BlackAndWhite, out byte[] bytes);

        result.SkipReason.Should().Be(CompressionSkipReason.Signed);
        bytes.Should().BeEmpty();
    }

    [Fact]
    public void BlackAndWhite_SkipsAnEncryptedDocument()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(PdfCompressorGuardTests.BuildEncryptedPdf()), "pw", leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(doc, output, PdfCompressionLevel.BlackAndWhite);

        result.SkipReason.Should().Be(CompressionSkipReason.Encrypted);
        output.Length.Should().Be(0);
    }

    [Fact]
    public void ReducedColor_WritesOneIndexedImage_AndKeepsColoursAndGreys()
    {
        CompressionResult result = Compress(DrawingPdf(rotate: 0), PdfCompressionLevel.ReducedColor, out byte[] bytes);

        result.Level.Should().Be(PdfCompressionLevel.ReducedColor);
        result.ImageDpi.Should().Be(150);
        result.BelowReadableResolution.Should().BeFalse();
        PdfStream image = PageImage(bytes);
        ((PdfInteger)image.Dictionary[PdfName.Intern("BitsPerComponent")]).Value.Should().Be(4);
        PdfArray space = (PdfArray)image.Dictionary[PdfName.Intern("ColorSpace")];
        space[0].Should().Be(PdfName.Intern("Indexed"));
        ((PdfInteger)space[2]).Value.Should().BeInRange(2, 15);

        PixelBuffer page = Render(bytes, 150);
        Rgb red = At(page, 200, 280);
        red.R.Should().BeGreaterThan(200, "the red line keeps its colour");
        red.G.Should().BeLessThan(80);
        Rgb grey = At(page, 300, 100);
        grey.Should().Be(new Rgb(0, 0, 0), "a 0.6 grey fill is darker than the snap-to-black luminance (D-277)");
    }

    [Fact]
    public void ReducedColor_DarkGreysBecomeBlack_LightGreysAndColoursStay()
    {
        // D-277: 0.5 grey (128) is below the snap luminance of 160; 0.75 grey
        // (191) is above it; a dark red is coloured, so it never snaps.
        byte[] source = PagePdf(
            "0.5 g 20 20 100 100 re f\n0.75 g 140 20 100 100 re f\n0.4 0 0 rg 260 20 100 100 re f\n", rotate: 0);
        Compress(source, PdfCompressionLevel.ReducedColor, out byte[] bytes);

        PixelBuffer page = Render(bytes, 150);
        At(page, 70, 70).Should().Be(new Rgb(0, 0, 0));
        Rgb light = At(page, 190, 70);
        light.R.Should().BeInRange(170, 215, "0.75 grey stays a light grey");
        Math.Abs(light.R - light.B).Should().BeLessThan(20);
        Rgb red = At(page, 310, 70);
        red.R.Should().BeInRange(80, 130, "dark red keeps its colour");
        red.G.Should().BeLessThan(30);
    }

    [Fact]
    public void ReducedColor_ThinBlackLine_HasNoDarkGreyEdges()
    {
        // A 1.3 pt black line at y = 150.1 is drawn three pixels high, its
        // lower edge pixel a dark grey (about 137). At reduced colour pixels
        // darker than 160 become black, so the line reads as black, not grey
        // (D-277).
        byte[] source = PagePdf("0 G 1.3 w 20 150.1 m 380 150.1 l S\n", rotate: 0);
        Compress(source, PdfCompressionLevel.ReducedColor, out byte[] bytes);

        PixelBuffer page = Render(bytes, 150);
        int column = (int)(200 * 150 / 72.0);
        int centre = (int)((300 - 150.1) * 150 / 72.0);
        int darkest = 255;
        for (int y = centre - 4; y <= centre + 4; y++)
        {
            (byte b, byte g, byte r, _) = page.GetPixelBgra(column, y);
            darkest = Math.Min(darkest, r);
            r.Should().NotBeInRange(1, 159, "no edge pixel stays dark grey");
            ((int)g).Should().Be(r);
            ((int)b).Should().Be(r);
        }

        darkest.Should().Be(0, "the line itself is black");
    }

    [Fact]
    public void BlackAndWhiteWithColor_KeepsColour_AndMakesGreysBlackAndWhite()
    {
        CompressionResult result = Compress(DrawingPdf(rotate: 0), PdfCompressionLevel.BlackAndWhiteWithColor, out byte[] bytes);

        result.Level.Should().Be(PdfCompressionLevel.BlackAndWhiteWithColor);
        PdfArray space = (PdfArray)PageImage(bytes).Dictionary[PdfName.Intern("ColorSpace")];
        space[0].Should().Be(PdfName.Intern("Indexed"));

        PixelBuffer page = Render(bytes, 150);
        Rgb red = At(page, 200, 280);
        red.R.Should().BeGreaterThan(200, "the red line keeps its colour");
        red.G.Should().BeLessThan(80);
        Rgb blue = At(page, 175, 85);
        blue.B.Should().BeGreaterThan(200, "the blue square keeps its colour");
        blue.R.Should().BeLessThan(80);

        int row = (int)((300 - 250) * 150 / 72.0);
        bool anyBlack = false;
        for (int dy = -2; dy <= 2; dy++)
        {
            anyBlack |= page.GetPixelBgra(150, row + dy).R < 64;
        }

        anyBlack.Should().BeTrue("the light grey line becomes black, as at BlackAndWhite");
        BlackShare(page, 260, 60, 340, 140).Should().BeInRange(0.2, 0.8, "the grey fill becomes a dot pattern");
    }

    [Fact]
    public void BlackAndWhiteWithColor_PageWithoutColour_IsTheOneBitImage()
    {
        Compress(DrawingPdf(rotate: 0, colours: false), PdfCompressionLevel.BlackAndWhiteWithColor, out byte[] withColour);
        Compress(DrawingPdf(rotate: 0, colours: false), PdfCompressionLevel.BlackAndWhite, out byte[] blackAndWhite);

        ((PdfInteger)PageImage(withColour).Dictionary[PdfName.Intern("BitsPerComponent")]).Value.Should().Be(1);
        withColour.Should().Equal(blackAndWhite);
    }

    [Fact]
    public void ImageLevels_AreSmallerInTurn_OnAColourDrawing()
    {
        byte[] source = DrawingPdf(rotate: 0, marks: 30_000);
        Compress(source, PdfCompressionLevel.ReducedColor, out byte[] reduced);
        Compress(source, PdfCompressionLevel.BlackAndWhiteWithColor, out byte[] withColour);
        Compress(source, PdfCompressionLevel.BlackAndWhite, out byte[] blackAndWhite);

        withColour.Length.Should().BeLessThan(reduced.Length);
        blackAndWhite.Length.Should().BeLessThan(withColour.Length);
    }

    [Fact]
    public void ImageOptions_Resolution_IsUsed_AndBelow130IsFlagged()
    {
        byte[] source = DrawingPdf(rotate: 0);
        CompressionResult at140 = Compress(source, PdfCompressionLevel.BlackAndWhite, new ImageLevelOptions { Dpi = 140 }, out byte[] bytes140);
        CompressionResult at100 = Compress(
            source, PdfCompressionLevel.BlackAndWhite, new ImageLevelOptions { Dpi = 100, AllowBelowReadableResolution = true }, out byte[] bytes100);

        at140.ImageDpi.Should().Be(140);
        at140.BelowReadableResolution.Should().BeFalse();
        ((PdfInteger)PageImage(bytes140).Dictionary[PdfName.Intern("Width")]).Value.Should().Be(778, "400 pt at 140 dpi");
        at100.ImageDpi.Should().Be(100);
        at100.BelowReadableResolution.Should().BeTrue();
        ((PdfInteger)PageImage(bytes100).Dictionary[PdfName.Intern("Width")]).Value.Should().Be(556);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(129.9, false)]
    [InlineData(49, true)]
    [InlineData(301, true)]
    [InlineData(double.NaN, true)]
    public void ImageOptions_OutOfRangeResolution_Throws(double dpi, bool evenWithLeave)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(DrawingPdf(0)), leaveOpen: false);
        Action without = () => PdfCompressor.Compress(doc, new MemoryStream(), PdfCompressionLevel.BlackAndWhite, new ImageLevelOptions { Dpi = dpi });
        Action with = () => PdfCompressor.Compress(
            doc, new MemoryStream(), PdfCompressionLevel.BlackAndWhite, new ImageLevelOptions { Dpi = dpi, AllowBelowReadableResolution = true });

        without.Should().Throw<ArgumentOutOfRangeException>();
        if (evenWithLeave)
        {
            with.Should().Throw<ArgumentOutOfRangeException>();
        }
        else
        {
            with.Should().NotThrow();
        }
    }

    [Fact]
    public void ImageOptions_AreIgnored_AtTheRewriteLevels()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(DrawingPdf(0)), leaveOpen: false);
        CompressionResult result = PdfCompressor.Compress(doc, new MemoryStream(), PdfCompressionLevel.Lossless, new ImageLevelOptions { Dpi = 10 });

        result.ImageDpi.Should().Be(0);
        result.BelowReadableResolution.Should().BeFalse();
    }

    [Fact]
    public void UnknownLevel_Throws()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(DrawingPdf(0)), leaveOpen: false);
        Action act = () => PdfCompressor.Compress(doc, new MemoryStream(), (PdfCompressionLevel)9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Target mode by level ────────────────────────────────────────────

    [Fact]
    public void Target_WithoutALevel_IsTheQualitySearch_AsBefore()
    {
        CompressToTargetResult result = Target(PhotoPdf(), 10_000_000, new CompressToTargetOptions(), out _);

        result.LevelUsed.Should().BeNull();
        result.QualityUsed.Should().BeGreaterThan(0, "the JPEG search ran");
    }

    [Fact]
    public void Target_TakesTheLightestLevelThatFits()
    {
        byte[] source = PhotoPdf();
        Compress(source, PdfCompressionLevel.Lossless, out byte[] lossless);
        Compress(source, PdfCompressionLevel.Medium, out byte[] medium);

        Target(source, lossless.Length, Levels(PdfCompressionLevel.BlackAndWhite), out _)
            .LevelUsed.Should().Be(PdfCompressionLevel.Lossless);

        CompressToTargetResult r = Target(source, medium.Length, Levels(PdfCompressionLevel.BlackAndWhite), out byte[] written);
        r.LevelUsed.Should().Be(PdfCompressionLevel.Medium);
        r.QualityUsed.Should().Be(75);
        r.TargetMet.Should().BeTrue();
        written.Length.Should().Be(medium.Length);
    }

    [Fact]
    public void Target_StopsAtMaxLevel()
    {
        CompressToTargetResult r = Target(PhotoPdf(), 100, Levels(PdfCompressionLevel.Medium), out _);

        r.LevelUsed.Should().Be(PdfCompressionLevel.Medium, "the smallest output allowed");
        r.TargetMet.Should().BeFalse();
    }

    [Fact]
    public void Target_WalksTheImageSteps_InOrder_AndTakesTheFirstThatFits()
    {
        byte[] source = DrawingPdf(rotate: 0, marks: 30_000);
        (PdfCompressionLevel Level, double Dpi)[] steps =
        {
            (PdfCompressionLevel.ReducedColor, 150),
            (PdfCompressionLevel.BlackAndWhiteWithColor, 150),
            (PdfCompressionLevel.ReducedColor, 130),
            (PdfCompressionLevel.BlackAndWhiteWithColor, 130),
            (PdfCompressionLevel.BlackAndWhite, 150),
            (PdfCompressionLevel.BlackAndWhite, 130),
        };
        long[] sizes = new long[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            Compress(source, steps[i].Level, new ImageLevelOptions { Dpi = steps[i].Dpi }, out byte[] bytes);
            sizes[i] = bytes.Length;
        }

        Compress(source, PdfCompressionLevel.High, out byte[] high);
        sizes.Should().OnlyContain(size => size < high.Length, "the image steps come after High");

        for (int k = 0; k < steps.Length; k++)
        {
            int expected = Array.FindIndex(sizes, size => size <= sizes[k]);
            CompressToTargetResult r = Target(source, sizes[k], Levels(PdfCompressionLevel.BlackAndWhite), out byte[] written);

            r.LevelUsed.Should().Be(steps[expected].Level);
            r.ImageDpiUsed.Should().Be(steps[expected].Dpi);
            r.TargetMet.Should().BeTrue();
            r.BelowReadableResolution.Should().BeFalse();
            written.Length.Should().Be((int)sizes[expected]);
        }
    }

    [Fact]
    public void Target_TriesBlackAndWhiteWithColorAt150_BeforeReducedColorAt130()
    {
        // D-274: a limit that reduced colour at 130 dpi would meet gets black
        // and white with colour at 150 dpi, which comes first and fits too.
        byte[] source = DrawingPdf(rotate: 0, marks: 30_000);
        Compress(source, PdfCompressionLevel.ReducedColor, new ImageLevelOptions { Dpi = 130 }, out byte[] reduced130);
        Compress(source, PdfCompressionLevel.BlackAndWhiteWithColor, out byte[] withColour150);
        withColour150.Length.Should().BeLessThan(reduced130.Length, "the drawing must make the order observable");

        CompressToTargetResult r = Target(source, reduced130.Length, Levels(PdfCompressionLevel.BlackAndWhite), out _);

        r.LevelUsed.Should().Be(PdfCompressionLevel.BlackAndWhiteWithColor);
        r.ImageDpiUsed.Should().Be(150);
    }

    [Fact]
    public void Target_NeverGoesBelow130_ByDefault_AndReportsNotMet()
    {
        CompressToTargetResult r = Target(DrawingPdf(0, marks: 30_000), 100, Levels(PdfCompressionLevel.BlackAndWhite), out byte[] written);

        r.LevelUsed.Should().Be(PdfCompressionLevel.BlackAndWhite);
        r.ImageDpiUsed.Should().Be(130, "the readable floor");
        r.BelowReadableResolution.Should().BeFalse();
        r.TargetMet.Should().BeFalse();
        r.FinalSize.Should().Be(written.Length);
    }

    [Fact]
    public void Target_WithLeave_StepsBelow130_ToTheGivenResolution_AndFlagsIt()
    {
        CompressToTargetOptions options = Levels(PdfCompressionLevel.BlackAndWhite) with
        {
            ImageOptions = new ImageLevelOptions { Dpi = 100, AllowBelowReadableResolution = true, Fills = BlackAndWhiteFills.Solid },
        };

        CompressToTargetResult r = Target(DrawingPdf(0, marks: 30_000), 100, options, out _);

        r.ImageDpiUsed.Should().Be(100);
        r.BelowReadableResolution.Should().BeTrue();
        r.TargetMet.Should().BeFalse();
    }

    [Fact]
    public void Target_StopsAtAColourImageLevel()
    {
        CompressToTargetResult r = Target(DrawingPdf(0, marks: 30_000), 100, Levels(PdfCompressionLevel.BlackAndWhiteWithColor), out _);

        r.LevelUsed.Should().Be(PdfCompressionLevel.BlackAndWhiteWithColor);
        r.ImageDpiUsed.Should().Be(130);
    }

    [Fact]
    public void Target_LowResolutionWithoutLeave_Throws()
    {
        CompressToTargetOptions options = Levels(PdfCompressionLevel.BlackAndWhite) with
        {
            ImageOptions = new ImageLevelOptions { Dpi = 100 },
        };

        Action act = () => Target(DrawingPdf(0), 100, options, out _);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Target_SignedDocument_IsWrittenUnchanged_AndReported()
    {
        CompressToTargetResult r = Target(PdfCompressorGuardTests.BuildSignedPdf(), 100, Levels(PdfCompressionLevel.BlackAndWhite), out _);

        r.SkipReason.Should().Be(CompressionSkipReason.Signed);
        r.LevelUsed.Should().BeNull();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static CompressToTargetOptions Levels(PdfCompressionLevel max) => new CompressToTargetOptions { MaxLevel = max };

    private static CompressionResult Compress(byte[] pdf, PdfCompressionLevel level, out byte[] written) =>
        Compress(pdf, level, null, out written);

    private static CompressionResult Compress(byte[] pdf, PdfCompressionLevel level, ImageLevelOptions? imageOptions, out byte[] written)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(doc, output, level, imageOptions);
        written = output.ToArray();
        return result;
    }

    private static CompressToTargetResult Target(byte[] pdf, long target, CompressToTargetOptions options, out byte[] written)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressToTargetResult result = PdfCompressor.CompressToTarget(doc, output, target, options);
        written = output.ToArray();
        return result;
    }

    private readonly record struct Rgb(int R, int G, int B);

    // The colour at a point given in points (y from the bottom of the 300 pt page), at 150 dpi.
    private static Rgb At(PixelBuffer page, double x, double y)
    {
        double k = 150 / 72.0;
        (byte b, byte g, byte r, byte _) = page.GetPixelBgra((int)(x * k), (int)((300 - y) * k));
        return new Rgb(r, g, b);
    }

    private static PdfStream PageImage(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PdfDictionary xobjects = doc.Objects.ResolveAs<PdfDictionary>(
            doc.Objects.ResolveAs<PdfDictionary>(doc.Pages[0].Dictionary[PdfName.Resources])![PdfName.XObject])!;
        return (PdfStream)doc.Objects.Resolve(xobjects[PdfName.Intern("Im0")]);
    }

    private static PixelBuffer Render(byte[] pdf, double dpi)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = dpi }).Rasterize(doc.Pages[0]);
    }

    // Share of black pixels in a rectangle given in points (y from the bottom of the 300 pt page).
    private static double BlackShare(PixelBuffer page, double x1, double y1, double x2, double y2)
    {
        double k = 150 / 72.0;
        int black = 0;
        int total = 0;
        for (int y = (int)((300 - y2) * k); y < (int)((300 - y1) * k); y++)
        {
            for (int x = (int)(x1 * k); x < (int)(x2 * k); x++)
            {
                total++;
                if (page.GetPixelBgra(x, y).R < 128)
                {
                    black++;
                }
            }
        }

        return (double)black / total;
    }

    // A page with a 600 x 600 noisy colour photo (JPEG quality 95).
    private static byte[] PhotoPdf()
    {
        Random random = new Random(7);
        PixelBuffer pixels = new PixelBuffer(600, 600);
        for (int y = 0; y < 600; y++)
        {
            for (int x = 0; x < 600; x++)
            {
                pixels.SetPixelBgra(x, y, (byte)random.Next(256), (byte)((x + y) / 5), (byte)(x / 3), 255);
            }
        }

        using MemoryStream jpeg = new MemoryStream();
        JpegEncoder.Encode(new ImageFrame(pixels, ImageColorFormat.Rgb24), jpeg, 95);
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(PageSize.A4).DrawImage(jpeg.ToArray(), 72, 72, 200, 200);
        return builder.ToByteArray();
    }

    // A 400 x 300 pt drawing: a 1 pt light-grey (0.8) line at y = 250, a 0.6
    // grey filled square (250, 50)-(350, 150), black text-like marks, and
    // optionally many tiny random marks so the vector file is large; with
    // colours, a 3 pt red line at y = 280 and a blue square (150, 60)-(200, 110).
    private static byte[] DrawingPdf(int rotate, int marks = 0, bool colours = true)
    {
        StringBuilder content = new StringBuilder();
        AppendDrawing(content, marks, colours);
        return PagePdf(content.ToString(), rotate);
    }

    private static void AppendDrawing(StringBuilder content, int marks, bool colours)
    {
        content.Append("0.8 G 1 w 20 250 m 380 250 l S\n");
        if (colours)
        {
            content.Append("1 0 0 RG 3 w 20 280 m 380 280 l S\n");
            content.Append("0 0 1 rg 150 60 50 50 re f\n");
        }

        content.Append("0.6 g 250 50 100 100 re f\n");
        content.Append("0 g 30 60 4 40 re f 40 60 4 40 re f 50 60 20 4 re f\n");
        Random random = new Random(3);
        for (int i = 0; i < marks; i++)
        {
            content.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{20 + (random.NextDouble() * 200):0.###} {160 + (random.NextDouble() * 80):0.###} 0.3 0.3 re f\n"));
        }
    }

    // A 400 x 300 pt page with the given content stream.
    private static byte[] PagePdf(string content, int rotate)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[] { new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(300) }));
        page.Set(PdfName.Intern("Rotate"), rotate);
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, bytes.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, bytes)),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        return output.ToArray();
    }
}
