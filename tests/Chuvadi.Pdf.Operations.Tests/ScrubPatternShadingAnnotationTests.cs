// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.22 Scrub residuals: tiling-pattern cells, mesh shadings and
// the appearances of annotations crossing the crop box keep only what can
// show inside the box; unused patterns and shadings, and the page
// thumbnail, leave the file.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ScrubPatternShadingAnnotationTests
{
    // Crop to the bottom-left 200x200 of a 400x400 page.
    private static readonly RectangleF Crop = new RectangleF(0, 0, 200, 200);
    private const string Media = "/MediaBox[0 0 400 400]";

    [Fact]
    public void TilingPatternCell_KeepsOnlyWhatShowsInTheBox()
    {
        // One 400x400 cell filling the page: its text at (300,300) can only
        // show outside the box.
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Pattern<</P0 5 0 R>>>>",
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 400 400]/XStep 400/YStep 400"
                    + "/Resources<</Font<</F1 6 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPCELL) Tj ET BT /F1 12 Tf 300 300 Td (SECRETCELL) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("SECRETCELL", "the off-box part of the cell must leave the file");
        output.Should().Contain("KEEPCELL");
    }

    [Fact]
    public void SmallRepeatingCell_IsKeptWhole()
    {
        // A 20x20 cell repeats across the box: every part of it shows.
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Pattern<</P0 5 0 R>>>>",
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 20 20]/XStep 20/YStep 20",
                "0 0 1 rg 0 0 10 10 re f % CELLMARK"));

        Scrub(pdf).Should().Contain("CELLMARK");
    }

    [Fact]
    public void UnusedPatternAndShading_LeaveTheFile()
    {
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Pattern<</P1 5 0 R>>/Shading<</Sh1 6 0 R>>>>",
            "0 0 1 rg 10 10 50 50 re f",
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 20 20]/XStep 20/YStep 20",
                "0 0 10 10 re f % UNUSEDPATTERN"),
            RawPdf.Obj("<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 1 0]/Function<</FunctionType 2"
                + "/Domain[0 1]/C0[0 0 0]/C1[1 1 1]/N 1>>/Extend[true true]/UNUSEDSHADING true>>"));

        string output = Scrub(pdf);

        output.Should().NotContain("UNUSEDPATTERN");
        output.Should().NotContain("UNUSEDSHADING");
    }

    [Fact]
    public void FreeFormMesh_KeepsOnlyTheInBoxTriangle()
    {
        // Two triangles (flag, x, y, r, g, b per vertex, 8 bits each, Decode
        // 0..400): one near the origin, one far outside carrying "QZX".
        byte[] data =
        {
            0, 10, 10, 0, 200, 0,
            0, 50, 10, 0, 200, 0,
            0, 10, 50, 0, 200, 0,
            0, 200, 200, (byte)'Q', (byte)'Z', (byte)'X',
            0, 250, 200, (byte)'Q', (byte)'Z', (byte)'X',
            0, 200, 250, (byte)'Q', (byte)'Z', (byte)'X',
        };
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Shading<</Sh0 5 0 R>>>>",
            "/Sh0 sh",
            RawPdf.Stream(
                "/ShadingType 4/ColorSpace/DeviceRGB/BitsPerCoordinate 8/BitsPerComponent 8/BitsPerFlag 8"
                    + "/Decode[0 400 0 400 0 1 0 1 0 1]",
                data));

        byte[] output = ScrubBytes(pdf);
        Encoding.Latin1.GetString(output).Should().NotContain("QZX");

        byte[] mesh = PageShading(output, "Sh0");
        mesh.Should().Equal(data[..18], "only the in-box triangle remains, byte for byte");
    }

    [Fact]
    public void LatticeMesh_BecomesTrianglesOfTheKeptCells()
    {
        // Four rows of two vertices at y = 0, 100, 300, 400 (raw 0, 64, 191,
        // 255 over 0..400); the top row carries "QZX" and only bounds the
        // cell lying wholly above the box.
        byte[] data =
        {
            0, 0, 0, 200, 0,     40, 0, 0, 200, 0,
            0, 64, 0, 200, 0,    40, 64, 0, 200, 0,
            0, 191, 0, 200, 0,   40, 191, 0, 200, 0,
            0, 255, (byte)'Q', (byte)'Z', (byte)'X',   40, 255, (byte)'Q', (byte)'Z', (byte)'X',
        };
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Shading<</Sh0 5 0 R>>>>",
            "/Sh0 sh",
            RawPdf.Stream(
                "/ShadingType 5/ColorSpace/DeviceRGB/BitsPerCoordinate 8/BitsPerComponent 8/VerticesPerRow 2"
                    + "/Decode[0 400 0 400 0 1 0 1 0 1]",
                data));

        byte[] output = ScrubBytes(pdf);
        Encoding.Latin1.GetString(output).Should().NotContain("QZX");

        using PdfDocument doc = Open(output);
        PdfStream shading = ShadingStream(doc, "Sh0");
        ((PdfInteger)shading.Dictionary[PdfName.Intern("ShadingType")]).Value.Should().Be(4);
        byte[] mesh = Inflate(shading.RawBytes);
        mesh.Length.Should().Be(4 * 3 * 6, "two kept cells of two triangles, three 6-byte vertices each");
        Encoding.Latin1.GetString(mesh).Should().NotContain("QZX");
    }

    [Fact]
    public void CoonsPatchSharingAnEdge_IsRewrittenStandAlone()
    {
        // Patch 1 (flag 0) lies wholly right of the box; patch 2 (flag 1)
        // shares patch 1's edge (points 3-6, colours 1-2) and reaches into
        // the box. Patch 1 is removed; patch 2 is written with flag 0 and
        // the shared edge spelled out.
        List<byte> data = new List<byte> { 0 };
        for (int k = 0; k < 12; k++)
        {
            data.Add((byte)(200 + k));
            data.Add((byte)(50 + k));
        }

        for (int k = 0; k < 4; k++)
        {
            data.AddRange(new[] { (byte)k, (byte)k, (byte)k });
        }

        data.Add(1);
        for (int k = 0; k < 8; k++)
        {
            data.Add((byte)(10 + k));
            data.Add((byte)(20 + k));
        }

        data.AddRange(new byte[] { 9, 9, 9, 8, 8, 8 });

        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Shading<</Sh0 5 0 R>>>>",
            "/Sh0 sh",
            RawPdf.Stream(
                "/ShadingType 6/ColorSpace/DeviceRGB/BitsPerCoordinate 8/BitsPerComponent 8/BitsPerFlag 8"
                    + "/Decode[0 400 0 400 0 1 0 1 0 1]",
                data.ToArray()));

        byte[] mesh = PageShading(ScrubBytes(pdf), "Sh0");

        List<byte> expected = new List<byte> { 0 };
        for (int k = 3; k <= 6; k++)
        {
            expected.Add((byte)(200 + k));
            expected.Add((byte)(50 + k));
        }

        for (int k = 0; k < 8; k++)
        {
            expected.Add((byte)(10 + k));
            expected.Add((byte)(20 + k));
        }

        expected.AddRange(new byte[] { 1, 1, 1, 2, 2, 2, 9, 9, 9, 8, 8, 8 });
        mesh.Should().Equal(expected);
    }

    [Fact]
    public void CrossingAnnotationAppearance_KeepsOnlyItsInBoxPart()
    {
        // Rect (150,150)-(250,250); the appearance's 100x100 BBox maps 1:1,
        // so its text at (10,10) shows at (160,160) and at (70,70) at (220,220).
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Annots[5 0 R]",
            "0 0 1 rg 10 10 50 50 re f",
            RawPdf.Obj("<</Type/Annot/Subtype/FreeText/Rect[150 150 250 250]/AP<</N 6 0 R>>/DA(/F1 10 Tf)>>"),
            RawPdf.Stream(
                "/Type/XObject/Subtype/Form/BBox[0 0 100 100]/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 10 Tf 10 10 Td (KEEPAP) Tj ET BT /F1 10 Tf 70 70 Td (SECRETANNOT) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("SECRETANNOT", "the off-box part of the appearance must leave the file");
        output.Should().Contain("KEEPAP");
    }

    [Fact]
    public void PatternCell_KeepsOnlyThePartPaintedInTheBox()
    {
        // The appearance fills (100,100)-(400,400) with a 300-point cell;
        // the neighbouring tile copy that would reach the box's bottom-left
        // is never painted, so the cell's far corner cannot show.
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Annots[5 0 R]",
            "0 g 1 1 2 2 re f",
            RawPdf.Obj("<</Type/Annot/Subtype/Square/Rect[100 100 400 400]/AP<</N 6 0 R>>>>"),
            RawPdf.Stream(
                "/Type/XObject/Subtype/Form/BBox[0 0 300 300]/Resources<</Pattern<</P0 7 0 R>>>>",
                "/Pattern cs /P0 scn 0 0 300 300 re f"),
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 300 300]/XStep 300/YStep 300"
                    + "/Resources<</Font<</F1 8 0 R>>>>",
                "BT /F1 12 Tf 10 10 Td (KEEPA) Tj ET BT /F1 12 Tf 250 250 Td (SECRETA) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("SECRETA");
        output.Should().Contain("KEEPA");
    }

    [Fact]
    public void OffBoxImageLabelledByTheStructureTree_LeavesTheFile()
    {
        byte[] data = new byte[2 * 2 * 3];
        for (int i = 0; i < data.Length; i += 3)
        {
            data[i] = (byte)'Q';
            data[i + 1] = (byte)'Z';
            data[i + 2] = (byte)'X';
        }

        byte[] pdf = RawPdf.Build(new List<byte[]>
        {
            RawPdf.Obj("<</Type/Catalog/Pages 2 0 R/StructTreeRoot 6 0 R>>"),
            RawPdf.Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            RawPdf.Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 5 0 R>>>>/Contents 4 0 R>>"),
            RawPdf.Stream(string.Empty, "q 100 0 0 100 280 280 cm /Im0 Do Q"),
            RawPdf.Stream("/Type/XObject/Subtype/Image/Width 2/Height 2/ColorSpace/DeviceRGB/BitsPerComponent 8", data),
            RawPdf.Obj("<</Type/StructTreeRoot/K 7 0 R>>"),
            RawPdf.Obj("<</Type/StructElem/S/Figure/P 6 0 R/K<</Type/OBJR/Obj 5 0 R>>>>"),
        });

        Scrub(pdf).Should().NotContain("QZX", "a structure label does not keep a removed image");
    }

    [Fact]
    public void SpacedTiles_KeepOnlyTheStripsThatShow()
    {
        // A 100-point cell repeating every 210 points, shifted left by 80:
        // the box shows cell x 80..100 (one tile) and 0..70 (the next);
        // text at cell x 72 never shows.
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</Pattern<</P0 5 0 R>>>>",
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 210/YStep 100"
                    + "/Matrix[1 0 0 1 -80 0]/Resources<</Font<</F1 6 0 R>>>>",
                "BT /F1 2 Tf 72 50 Td (QZXS) Tj ET BT /F1 2 Tf 10 50 Td (KEEPL) Tj ET BT /F1 2 Tf 85 50 Td (KEEPR) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("QZXS", "the gap between the visible strips never shows");
        output.Should().Contain("KEEPL").And.Contain("KEEPR");
    }

    [Fact]
    public void RotatedPatternCell_KeepsOnlyWhatShows()
    {
        // Rotated 45 degrees: the visible parts of the cell are triangles;
        // cell point (100,100) lands outside the box in every tile copy.
        byte[] pdf = RawPdf.SinglePage(
            "/MediaBox[0 0 200 200]/Resources<</Pattern<</P0 5 0 R>>>>",
            "/Pattern cs /P0 scn 0 0 200 200 re f",
            RawPdf.Stream(
                "/Type/Pattern/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 300 300]/XStep 300/YStep 300"
                    + "/Matrix[0.7071068 0.7071068 -0.7071068 0.7071068 100 100]/Resources<</Font<</F1 6 0 R>>>>",
                "BT /F1 2 Tf 100 100 Td (QZXR) Tj ET BT /F1 2 Tf 20 20 Td (KEEPR) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("QZXR");
        output.Should().Contain("KEEPR");
    }

    [Fact]
    public void AnnotationInsideTheBox_DropsAppearanceContentDrawnOffBox()
    {
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Annots[5 0 R]",
            "0 g 1 1 2 2 re f",
            RawPdf.Obj("<</Type/Annot/Subtype/FreeText/Rect[50 50 90 90]/AP<</N 6 0 R>>>>"),
            RawPdf.Stream(
                "/Type/XObject/Subtype/Form/BBox[0 0 40 40]/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 10 Tf 5 5 Td (KEEP) Tj ET BT /F1 10 Tf 300 300 Td (QZXIN) Tj ET"),
            Helvetica());

        string output = Scrub(pdf);

        output.Should().NotContain("QZXIN");
        output.Should().Contain("KEEP");
    }

    [Fact]
    public void PageThumbnail_LeavesTheFile()
    {
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Thumb 5 0 R",
            "0 0 1 rg 10 10 50 50 re f",
            RawPdf.Stream("/Width 2/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8", "THUMBX"));

        Scrub(pdf).Should().NotContain("THUMBX", "a thumbnail pictures the uncropped page");
    }

    private static byte[] Helvetica() => RawPdf.Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

    private static string Scrub(byte[] pdf) => Encoding.Latin1.GetString(ScrubBytes(pdf));

    private static byte[] ScrubBytes(byte[] pdf)
    {
        using PdfDocument doc = Open(pdf);
        using MemoryStream output = new MemoryStream();
        PageCropper.Crop(output, doc, new List<PageCrop> { new PageCrop(0, Crop, PageCropMode.Scrub) });
        return output.ToArray();
    }

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static byte[] PageShading(byte[] pdf, string name)
    {
        using PdfDocument doc = Open(pdf);
        return Inflate(ShadingStream(doc, name).RawBytes);
    }

    private static PdfStream ShadingStream(PdfDocument doc, string name)
    {
        PdfDictionary shadings = (PdfDictionary)doc.Objects.Resolve(doc.Pages[0].Resources![PdfName.Intern("Shading")]);
        return (PdfStream)doc.Objects.Resolve(shadings[PdfName.Intern(name)]);
    }

    private static byte[] Inflate(byte[] data)
    {
        using MemoryStream input = new MemoryStream(data);
        using ZLibStream z = new ZLibStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}
