// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.22.1: the content of a tiling-pattern cell that a redaction
// box would show is removed from the cell, not just the painted area.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionPatternCellTests
{
    private const string Media = "/MediaBox[0 0 400 400]";

    // One 400x400 cell (a single tile) filling the page.
    private const string BigCell =
        "/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 400 400]/XStep 400/YStep 400";

    [Fact]
    public void TextInABigCell_UnderTheBox_LeavesTheFile()
    {
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream(BigCell + "/Resources<</Font<</F1 6 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica());
        RectangleF box = new RectangleF(250, 290, 140, 40);

        string output = Latin1(Redact(input, box));

        output.Should().NotContain("CELLSECRET");
        output.Should().Contain("KEEPTEXT", "cell content outside the box still shows");
        using PdfDocument doc = Open(Encoding.Latin1.GetBytes(output));
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, box) }).IsClean.Should().BeTrue();
    }

    [Fact]
    public void ImageInABigCell_IsBlankedWhereTheBoxCoversIt()
    {
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream(BigCell + "/Resources<</XObject<</Im0 6 0 R>>>>", "q 100 0 0 100 100 100 cm /Im0 Do Q"),
            MarkerImage());
        RectangleF box = new RectangleF(90, 50, 60, 300);

        byte[] output = Redact(input, box);

        Latin1(output).Should().NotContain("QZX", "the covered half of the cell's image must leave the file");
        using PdfDocument doc = Open(output);
        PdfStream cell = PatternOf(doc, "P0");
        PdfDictionary cellResources = (PdfDictionary)doc.Objects.Resolve(cell.Dictionary[PdfName.Resources]);
        PdfDictionary xobjects = (PdfDictionary)doc.Objects.Resolve(cellResources[PdfName.XObject]);
        byte[]? samples = null;
        foreach (PdfName key in xobjects.Keys)
        {
            if (doc.Objects.Resolve(xobjects[key]) is PdfStream image)
            {
                samples = Inflate(image.RawBytes);
            }
        }

        samples.Should().NotBeNull();
        samples![0].Should().Be(0, "a covered pixel");
        Encoding.Latin1.GetString(samples, 27, 3).Should().Be("KEP", "a kept pixel");
        RedactionVerifier.Verify(doc, new List<RedactionRect> { new RedactionRect(0, box) }).IsClean.Should().BeTrue();
    }

    [Fact]
    public void SmallHatchShownWholeElsewhere_IsLeftUnchanged()
    {
        // A 10-point hatch filling the page: every tile shows the whole cell,
        // so the box hides nothing that is not visible elsewhere.
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 10 10]/XStep 10/YStep 10/Resources<<>>",
                "0 0 5 10 re f"));

        using PdfDocument doc = Open(Redact(input, new RectangleF(100, 100, 50, 50)));
        PdfDictionary resources = doc.Pages[0].Resources!;
        PdfDictionary patterns = (PdfDictionary)doc.Objects.Resolve(resources[PdfName.Intern("Pattern")]);
        ((PdfReference)patterns[PdfName.Intern("P0")]).ObjectId.ObjectNumber.Should().Be(5, "the original cell is kept");
    }

    [Fact]
    public void PatternUsedOnlyOutsideTheBox_IsLeftUnchanged()
    {
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 100 100 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream(BigCell + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 12 Tf 260 300 Td (CELLTEXT) Tj ET"),
            Helvetica());

        Latin1(Redact(input, new RectangleF(250, 290, 140, 40))).Should().Contain(
            "CELLTEXT", "the pattern is never painted under the box");
    }

    [Fact]
    public void TextInACellPaintedInsideAForm_LeavesTheFile()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Fm0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "/Fm0 Do"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</Pattern<</P0 6 0 R>>>>",
                "/Pattern cs /P0 scn 0 0 400 400 re f"),
            Stream(BigCell + "/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica(),
        });

        string output = Latin1(Redact(input, new RectangleF(250, 290, 140, 40)));

        output.Should().NotContain("CELLSECRET");
        output.Should().Contain("KEEPTEXT");
    }

    [Fact]
    public void TextInANestedPatternCell_LeavesTheFile()
    {
        // P0's cell fills itself with P1, whose cell holds the text.
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream(BigCell + "/Resources<</Pattern<</P1 6 0 R>>>>", "/Pattern cs /P1 scn 0 0 400 400 re f"),
            Stream(BigCell + "/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica());

        string output = Latin1(Redact(input, new RectangleF(250, 290, 140, 40)));

        output.Should().NotContain("CELLSECRET");
        output.Should().Contain("KEEPTEXT");
    }

    [Theory]
    [InlineData("q /Pattern cs /P0 scn 1000 1000 100 100 re f Q")]
    [InlineData("q /G0 gs /Pattern cs /P0 scn 0 0 100 100 re f Q")]
    [InlineData("/OC /L0 BDC q /Pattern cs /P0 scn 0 0 100 100 re f Q EMC")]
    [InlineData("q BT 7 Tr /F1 1 Tf 390 390 Td (.) Tj ET /Pattern cs /P0 scn 0 0 100 100 re f Q")]
    public void AFillThatMayNotShow_DoesNotKeepTheCellWhole(string decoy)
    {
        // A 100-point cell: the real fill shows it only inside the box; the
        // decoy would show a whole tile elsewhere, but off the page, under an
        // ExtGState, in a layer or under a text clip it may not show at all.
        byte[] input = Page(
            decoy + " /Pattern cs /P0 scn 200 200 100 100 re f",
            "/Pattern<</P0 5 0 R>>/Font<</F1 6 0 R>>/ExtGState<</G0<</ca 0>>>>/Properties<</L0 7 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica(),
            Obj("<</Type/OCG/Name(L0)>>"));

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain("QZXP");
    }

    [Theory]
    [InlineData("/Pattern cs /P0 scn 0 0 100 100 re 4 0 0 4 0 0 cm f")]
    [InlineData("/Pattern cs /P0 scn q 0.25 0 0 0.25 0 0 cm 0 0 400 400 re Q f")]
    public void TransformInsideThePath_CountsWhereTheFillReallyLands(string content)
    {
        // A CTM change between construction and "f" (a cm, or a Q restoring
        // another CTM) puts the fill at 0..400, so the pattern does paint
        // inside the box.
        byte[] input = Page(
            content,
            "/Pattern<</P0 5 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica());

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain("QZXP");
    }

    [Fact]
    public void ATileOutsideTheCropBox_DoesNotKeepTheCellWhole()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/CropBox[100 100 400 400]"
                + "/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty,
                "q /Pattern cs /P0 scn 0 0 100 100 re f Q q /Pattern cs /P0 scn 200 200 100 100 re f Q"),
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica(),
        });

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain(
            "QZXP", "the tile at 0..100 is outside the crop box and never shows");
    }

    [Theory]
    [InlineData("1 g 0 0 100 100 re f")]
    [InlineData("BT /F1 30 Tf 10 40 Td (WWW) Tj ET")]
    [InlineData("q 100 0 0 100 0 0 cm /Im1 Do Q")]
    public void ADecoyTilePaintedOverLater_DoesNotKeepTheCellWhole(string overpaint)
    {
        // The tile at 0..100 would show the whole cell, but opaque content
        // painted over it afterwards hides (some of) it.
        byte[] input = Page(
            "q /Pattern cs /P0 scn 0 0 100 100 re f Q q " + overpaint + " Q /Pattern cs /P0 scn 200 200 100 100 re f",
            "/Pattern<</P0 5 0 R>>/Font<</F1 6 0 R>>/XObject<</Im1 7 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica(),
            Stream("/Type/XObject/Subtype/Image/Width 1/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", "\u00ff"));

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain("QZXP");
    }

    [Fact]
    public void ADecoyTileUnderAnAnnotation_DoesNotKeepTheCellWhole()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media + "/Annots[7 0 R]"
                + "/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty,
                "q /Pattern cs /P0 scn 0 0 100 100 re f Q /Pattern cs /P0 scn 200 200 100 100 re f"),
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica(),
            Obj("<</Type/Annot/Subtype/Square/Rect[0 0 100 100]/AP<</N 8 0 R>>>>"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 100 100]", "1 g 0 0 100 100 re f"),
        });

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain("QZXP");
    }

    [Theory]
    [InlineData("BT /T3 1 Tf -50 -50 Td (a) Tj ET")]
    [InlineData("/GD gs /Pattern cs /P0 scn 0 0 100 100 re f")]
    public void ADecoyTileCoveredByType3TextOrABlendedRepaint_DoesNotKeepTheCellWhole(string overpaint)
    {
        byte[] input = Page(
            "q /Pattern cs /P0 scn 0 0 100 100 re f Q q " + overpaint + " Q /Pattern cs /P0 scn 200 200 100 100 re f",
            "/Pattern<</P0 5 0 R>>/Font<</T3 7 0 R>>/ExtGState<</GD<</BM/Difference>>>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 100 100]/XStep 100/YStep 100"
                + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 10 Tf 10 50 Td (QZXP) Tj ET"),
            Helvetica(),
            Obj("<</Type/Font/Subtype/Type3/FontBBox[0 0 100 100]/FontMatrix[1 0 0 1 0 0]/CharProcs<</a 8 0 R>>"
                + "/Encoding<</Type/Encoding/Differences[97/a]>>/FirstChar 97/LastChar 97/Widths[100]>>"),
            Stream(string.Empty, "100 0 0 0 100 100 d1 50 50 100 100 re f"));

        Latin1(Redact(input, new RectangleF(200, 200, 100, 100))).Should().NotContain("QZXP");
    }

    [Fact]
    public void PatternDrawnByAResourceInheritingForm_IsRedacted()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media
                + "/Resources<</XObject<</Fm0 5 0 R>>/Pattern<</P0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, "/Fm0 Do"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "/Pattern cs /P0 scn 0 0 400 400 re f"),
            Stream(BigCell + "/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica(),
        });

        string output = Latin1(Redact(input, new RectangleF(250, 290, 140, 40)));

        output.Should().NotContain("CELLSECRET");
        output.Should().Contain("KEEPTEXT");
    }

    [Theory]
    [InlineData("/Fm0 Do")]
    [InlineData("/Fm0 Do /Pattern cs /P0 scn 380 320 20 20 re f")]
    public void PatternDrawnByAFormSharedByPagesWithDifferentResources_IsRedacted(string pageContent)
    {
        // Fm0 inherits its resources, and the two pages give it different
        // ones, so it cannot be given a copy of its own.
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R 8 0 R]/Count 2>>"),
            Obj("<</Type/Page/Parent 2 0 R" + Media
                + "/Resources<</XObject<</Fm0 5 0 R>>/Pattern<</P0 6 0 R>>>>/Contents 4 0 R>>"),
            Stream(string.Empty, pageContent),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]", "/Pattern cs /P0 scn 0 0 400 400 re f"),
            Stream(BigCell + "/Resources<</Font<</F1 7 0 R>>>>",
                "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica(),
            Obj("<</Type/Page/Parent 2 0 R" + Media
                + "/Resources<</XObject<</Fm0 5 0 R>>/Pattern<</P0 10 0 R>>/Font<</F1 7 0 R>>>>/Contents 9 0 R>>"),
            Stream(string.Empty, "/Fm0 Do"),
            Stream(BigCell + "/Resources<</Font<</F1 7 0 R>>>>", "BT /F1 12 Tf 260 300 Td (OTHERCELL) Tj ET"),
        });

        string output = Latin1(Redact(input, new RectangleF(250, 290, 140, 40)));

        output.Should().NotContain("CELLSECRET");
        output.Should().Contain("KEEPTEXT");
        output.Should().Contain("OTHERCELL", "page 2 is not redacted and draws its own pattern");
    }

    [Fact]
    public void Verifier_FindsTextInAPatternCell()
    {
        byte[] input = Page(
            "/Pattern cs /P0 scn 0 0 400 400 re f",
            "/Pattern<</P0 5 0 R>>",
            Stream(BigCell + "/Resources<</Font<</F1 6 0 R>>>>", "BT /F1 12 Tf 260 300 Td (CELLSECRET) Tj ET"),
            Helvetica());

        using PdfDocument doc = Open(input);
        RedactionVerifier.ExtractWithin(doc, 0, new RectangleF(250, 290, 140, 40))
            .Should().Contain(s => s.Kind == RedactionSurvivorKind.Text);
        RedactionVerifier.ExtractWithin(doc, 0, new RectangleF(0, 0, 100, 100))
            .Should().NotContain(s => s.Kind == RedactionSurvivorKind.Text, "no cell text lies there");
    }

    private static PdfStream PatternOf(PdfDocument doc, string name)
    {
        PdfDictionary resources = doc.Pages[0].Resources!;
        PdfDictionary patterns = (PdfDictionary)doc.Objects.Resolve(resources[PdfName.Intern("Pattern")]);
        return (PdfStream)doc.Objects.Resolve(patterns[PdfName.Intern(name)]);
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

    // One page (objects 1-4) followed by extra objects numbered from 5.
    private static byte[] Page(string content, string resources, params byte[][] extra)
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

    private static byte[] Helvetica() => Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

    // A 10x10 RGB image: left half pixels "QZX", right half "KEP".
    private static byte[] MarkerImage()
    {
        byte[] data = new byte[10 * 10 * 3];
        for (int i = 0; i < 100; i++)
        {
            string marker = (i % 10) < 5 ? "QZX" : "KEP";
            data[i * 3] = (byte)marker[0];
            data[(i * 3) + 1] = (byte)marker[1];
            data[(i * 3) + 2] = (byte)marker[2];
        }

        return Stream("/Type/XObject/Subtype/Image/Width 10/Height 10/ColorSpace/DeviceRGB/BitsPerComponent 8", data);
    }

    private static byte[] Inflate(byte[] data)
    {
        using MemoryStream input = new MemoryStream(data);
        using ZLibStream z = new ZLibStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
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
