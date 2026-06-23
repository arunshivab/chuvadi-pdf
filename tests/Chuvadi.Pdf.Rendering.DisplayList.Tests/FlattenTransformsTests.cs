// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM), §8.4.4 (q/Q)
//
// Verifies DisplayListBuilder.Build(..., flattenTransforms: true): the returned
// list contains no TransformOps, while every PathOp's baked geometry and CTM
// are preserved (the CTM is already per-op), so extraction is unchanged. The
// default (false) keeps the full op stream for renderers that track state.

using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class FlattenTransformsTests
{
    private const string ContentWithQcmQ = "q\n2 0 0 2 10 20 cm\n5 5 m\n15 5 l\nS\nQ";

    [Fact]
    public void Default_KeepsTransformOps()
    {
        using PdfDocument doc = BuildPage(ContentWithQcmQ);

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        list.Count(op => op is TransformOp).Should().BeGreaterThan(0);
    }

    [Fact]
    public void FlattenTransforms_RemovesAllTransformOps()
    {
        using PdfDocument doc = BuildPage(ContentWithQcmQ);

        PageDisplayList flat = DisplayListBuilder.Build(doc, 0, flattenTransforms: true);

        flat.Should().NotContain(op => op is TransformOp);
        flat.OfType<PathOp>().Should().ContainSingle();
    }

    [Fact]
    public void FlattenTransforms_PreservesPathGeometryAndCtm()
    {
        using PdfDocument doc = BuildPage(ContentWithQcmQ);

        PathOp normal = DisplayListBuilder.Build(doc, 0).OfType<PathOp>().Single();
        PathOp flat = DisplayListBuilder.Build(doc, 0, flattenTransforms: true).OfType<PathOp>().Single();

        flat.Ctm.Should().Be(normal.Ctm);
        flat.Geometry.Segments.Should().HaveCount(normal.Geometry.Segments.Count);
        flat.RawGeometry.Should().NotBeNull();
        // CTM is baked per-op: applying it to the raw start point gives the page-space point.
        (double px, double py) = flat.Ctm.Apply(
            flat.RawGeometry!.Segments[0].X1, flat.RawGeometry.Segments[0].Y1);
        px.Should().BeApproximately(flat.Geometry.Segments[0].X1, 1e-9);
        py.Should().BeApproximately(flat.Geometry.Segments[0].Y1, 1e-9);
    }

    [Fact]
    public void FlattenTransforms_DoesNotChangeExtractedSegments()
    {
        using PdfDocument doc = BuildPage(ContentWithQcmQ);

        int normal = DisplayListBuilder.Build(doc, 0).ExtractLineSegments().Count;
        int flat = DisplayListBuilder.Build(doc, 0, flattenTransforms: true).ExtractLineSegments().Count;

        flat.Should().Be(normal);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static PdfDocument BuildPage(string content)
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] bytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, bytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, bytes);

        PdfIndirectObject[] objects =
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
