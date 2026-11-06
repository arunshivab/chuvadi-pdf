// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (P1) — SVG of a page whose MediaBox does not start at 0,0

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgPageOrigin3312Tests
{
    [Fact]
    public void OffsetMediaBox_PageFlipTranslatesByTheBoxCorner()
    {
        using PdfDocument doc = PdfDocument.Open(Build(50, 450, 250, 650), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        // y' = -y + (450 + 200) and x' = x - 50, so the box's lower-left
        // corner lands on the SVG origin's bottom-left.
        svg.Should().Contain("<g transform=\"matrix(1 0 0 -1 -50 650)\">");
    }

    [Fact]
    public void ZeroOriginMediaBox_PageFlipIsUnchanged()
    {
        using PdfDocument doc = PdfDocument.Open(Build(0, 0, 200, 200), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<g transform=\"matrix(1 0 0 -1 0 200)\">");
    }

    private static MemoryStream Build(int x1, int y1, int x2, int y2)
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
            new PdfInteger(x1), new PdfInteger(y1), new PdfInteger(x2), new PdfInteger(y2),
        }));
        page.Set(PdfName.Resources, new PdfDictionary());
        page.Set(PdfName.Contents, new PdfReference(4, 0));

        byte[] content = Encoding.ASCII.GetBytes("1 0 0 rg 100 500 100 100 re f");
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
