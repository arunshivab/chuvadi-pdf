// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (paths), §8.5.3 (path-painting operators)
// PHASE: Redaction R1 — vector-graphics in-region removal
//
// A painted path (fill/stroke) whose geometry falls inside a redaction region
// must be removed from the content stream, not merely covered by the overlay.
// A clipping path (W n) draws nothing and must be preserved — dropping a clip
// could expose content. Each test asserts on the path's distinctive operand
// bytes in the (uncompressed) redacted content stream.

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

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionVectorTests
{
    // Distinctive operands so the check can't collide with the overlay or media box.
    private const string InRegionPath = "123 705 44 11";
    private const string OutRegionPath = "123 105 44 11";

    [Fact]
    public void Apply_FilledPathInRegion_IsRemoved()
    {
        // Two filled rectangles: one inside the region (y 705), one far below.
        using MemoryStream source = BuildVectorPdf(
            "q\n123 705 44 11 re\nf\n123 105 44 11 re\nf\nQ\n");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Redactor.Apply(output, doc, RegionOptions());

        string bytes = Encoding.Latin1.GetString(output.ToArray());
        bytes.Should().NotContain(InRegionPath,
            "a filled path inside a redaction region must be removed from the content stream");
        bytes.Should().Contain(OutRegionPath,
            "a filled path outside every region must be preserved");
    }

    [Fact]
    public void Apply_ClippingPathInRegion_IsPreserved()
    {
        // A clip path (W n) in the region draws nothing; dropping it could
        // expose content, so it must survive.
        using MemoryStream source = BuildVectorPdf(
            "q\n123 705 44 11 re\nW\nn\nQ\n");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Redactor.Apply(output, doc, RegionOptions());

        string bytes = Encoding.Latin1.GetString(output.ToArray());
        bytes.Should().Contain(InRegionPath,
            "a clipping path must be preserved even when it lies inside a region");
    }

    private static RedactionOptions RegionOptions()
    {
        return new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(90, 690, 220, 30)),
            },
        };
    }

    private static MemoryStream BuildVectorPdf(string contentStream)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] content = Encoding.ASCII.GetBytes(contentStream);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
