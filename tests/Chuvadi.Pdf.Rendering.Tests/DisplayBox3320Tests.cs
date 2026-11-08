// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-221 P2, D-230 Q8) — the CropBox is the default display area

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class DisplayBox3320Tests
{
    // MediaBox 0 0 400 400; CropBox 100 100 300 300. Red square 150..250 in
    // both directions (inside the crop); blue square 0..80 (outside it).
    private const string Drawing = "1 0 0 rg 150 150 100 100 re f 0 0 1 rg 0 0 80 80 re f";

    [Fact]
    public void Default_RendersTheCropBox_FromItsCorner()
    {
        PixelBuffer buffer = Render(new[] { 100, 100, 300, 300 }, new RenderOptions { Dpi = 72 });

        buffer.Width.Should().Be(200);
        buffer.Height.Should().Be(200);
        IsRed(buffer.GetPixelBgra(100, 100)).Should().BeTrue("the red square is at the crop's centre");
        IsWhite(buffer.GetPixelBgra(20, 180)).Should().BeTrue("the blue square is outside the crop and not drawn");
    }

    [Fact]
    public void MediaBoxOption_RendersTheWholeMediaBox_AsBefore()
    {
        PixelBuffer buffer = Render(new[] { 100, 100, 300, 300 }, new RenderOptions { Dpi = 72, DisplayBox = PdfDisplayBox.MediaBox });

        buffer.Width.Should().Be(400);
        buffer.Height.Should().Be(400);
        IsRed(buffer.GetPixelBgra(200, 200)).Should().BeTrue();
        IsBlue(buffer.GetPixelBgra(40, 360)).Should().BeTrue("the blue square shows on the full MediaBox");
    }

    [Fact]
    public void NoCropBox_RendersTheMediaBox()
    {
        PixelBuffer buffer = Render(null, new RenderOptions { Dpi = 72 });

        buffer.Width.Should().Be(400);
        buffer.Height.Should().Be(400);
    }

    [Fact]
    public void CropBoxLargerThanTheMediaBox_IsIntersectedWithIt()
    {
        PixelBuffer buffer = Render(new[] { -100, -100, 300, 500 }, new RenderOptions { Dpi = 72 });

        buffer.Width.Should().Be(300);
        buffer.Height.Should().Be(400);
    }

    [Fact]
    public void CropBoxOutsideTheMediaBox_FallsBackToTheMediaBox()
    {
        PixelBuffer buffer = Render(new[] { 500, 500, 600, 600 }, new RenderOptions { Dpi = 72 });

        buffer.Width.Should().Be(400);
        buffer.Height.Should().Be(400);
    }

    [Fact]
    public void SuperSampledRender_UsesTheSameBox()
    {
        PixelBuffer buffer = Render(new[] { 100, 100, 300, 300 }, new RenderOptions { Dpi = 72, SuperSample = 2 });

        buffer.Width.Should().Be(200);
        IsRed(buffer.GetPixelBgra(100, 100)).Should().BeTrue();
    }

    [Fact]
    public void PageRotation_RotatesTheCroppedPage()
    {
        PixelBuffer buffer = Render(new[] { 100, 100, 300, 200 }, new RenderOptions { Dpi = 72, Rotation = 90 });

        buffer.Width.Should().Be(100);
        buffer.Height.Should().Be(200);
    }

    private static PixelBuffer Render(int[]? cropBox, RenderOptions options)
    {
        using PdfDocument doc = PdfDocument.Open(Build(cropBox), leaveOpen: false);
        return new PageRasterizer(doc.Objects, options).Rasterize(doc.Pages[0]);
    }

    private static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;

    private static bool IsBlue((byte B, byte G, byte R, byte A) p) => p.B > 200 && p.R < 60 && p.G < 60;

    private static bool IsWhite((byte B, byte G, byte R, byte A) p) => p.R > 240 && p.G > 240 && p.B > 240;

    internal static MemoryStream Build(int[]? cropBox)
    {
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
        page.Set(PdfName.MediaBox, Box(0, 0, 400, 400));
        if (cropBox is not null)
        {
            page.Set(PdfName.CropBox, Box(cropBox[0], cropBox[1], cropBox[2], cropBox[3]));
        }

        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));

        byte[] content = Encoding.ASCII.GetBytes(Drawing);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }

    private static PdfArray Box(int x1, int y1, int x2, int y2) =>
        new PdfArray(new PdfPrimitive[] { new PdfInteger(x1), new PdfInteger(y1), new PdfInteger(x2), new PdfInteger(y2) });
}
