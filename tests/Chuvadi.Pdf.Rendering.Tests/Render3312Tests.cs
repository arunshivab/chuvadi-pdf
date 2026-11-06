// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 — pages whose MediaBox does not start at 0,0 (P1), and the
//        thin-span rasterizer fix it needs

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

public sealed class Render3312Tests
{
    // ── P1: MediaBox origin ──────────────────────────────────────────────

    [Fact]
    public void Rasterize_PageWhoseMediaBoxDoesNotStartAtZero_DrawsTheContentWhereItIs()
    {
        // MediaBox [50 450 250 650]: 200 x 200 points. The red square
        // 100 500 100 100 is 50 points in from the left and 50 down from the
        // top, so at 72 dpi it covers pixels 50..149 in both directions.
        using MemoryStream pdf = BuildOffsetPage();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);

        buffer.Width.Should().Be(200);
        buffer.Height.Should().Be(200);
        IsRed(buffer.GetPixelBgra(100, 100)).Should().BeTrue("the square's centre is at pixel 100,100");
        IsRed(buffer.GetPixelBgra(55, 55)).Should().BeTrue("the square starts at pixel 50,50");
        IsWhite(buffer.GetPixelBgra(25, 100)).Should().BeTrue("left of the square is blank");
        IsWhite(buffer.GetPixelBgra(100, 175)).Should().BeTrue("below the square is blank");
    }

    [Fact]
    public void Rasterize_ClipOnTheLeftEdgeOfAnOffsetBox_DrawsTheStrokeWithoutThrowing()
    {
        // The layout PageCropper's ClipOnly writes: the MediaBox is the crop
        // box, and the content is clipped to the same rectangle. Drawn from
        // the box's corner (P1), the clip edge lands exactly on x = 0, and the
        // stroke crossing it made a span thinner than the rasterizer's 1e-9
        // guard there, which indexed column -1 and threw.
        using MemoryStream pdf = BuildOffsetPage(
            new[] { 50, 450, 400, 770 },
            "q 50 450 350 320 re W n q 0 0 1 RG 3 w 20 520 m 500 720 l S Q Q");
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);

        // The line enters the box at x = 50, y = 532.5: pixel row 770 - 532.5.
        bool blueNearLeftEdge = false;
        for (int y = 234; y <= 241; y++)
        {
            (byte b, byte g, byte r, byte _) = buffer.GetPixelBgra(2, y);
            blueNearLeftEdge |= b > 150 && r < 120 && g < 120;
        }

        blueNearLeftEdge.Should().BeTrue("the stroke is drawn up to the clip edge at x = 0");
    }

    private static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 200 && p.G < 50 && p.B < 50;

    private static bool IsWhite((byte B, byte G, byte R, byte A) p) => p.R > 240 && p.G > 240 && p.B > 240;

    internal static MemoryStream BuildOffsetPage() =>
        BuildOffsetPage(new[] { 50, 450, 250, 650 }, "1 0 0 rg 100 500 100 100 re f");

    internal static MemoryStream BuildOffsetPage(int[] mediaBox, string drawing)
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
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(mediaBox[0]), new PdfInteger(mediaBox[1]), new PdfInteger(mediaBox[2]), new PdfInteger(mediaBox[3]),
        }));
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));

        byte[] content = Encoding.ASCII.GetBytes(drawing);
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
}
