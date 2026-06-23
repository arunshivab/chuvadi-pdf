// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM), §8.5.2 (path construction)
//
// Verifies that PathOp retains the user-space (pre-CTM) geometry alongside the
// baked page-space geometry, together with the CTM that maps one to the other:
// Ctm.Apply(raw) == baked for every constructed point.

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

public sealed class PathRawGeometryTests
{
    [Fact]
    public void LinePath_UnderScaleAndTranslate_RetainsRawGeometryAndCtm()
    {
        // cm = 2 0 0 2 10 20; then m (5,5) l (15,25) S.
        using PdfDocument doc = BuildPageWithContent(
            "2 0 0 2 10 20 cm\n5 5 m\n15 25 l\nS");

        PathOp path = SinglePath(doc);

        path.Ctm.Should().Be(new AffineMatrix(2, 0, 0, 2, 10, 20));

        path.RawGeometry.Should().NotBeNull();
        path.RawGeometry!.Segments.Should().HaveCount(2);
        path.RawGeometry.Segments[0].Command.Should().Be(PathCommand.MoveTo);
        AssertPoint(path.RawGeometry, 0, 5, 5);
        AssertPoint(path.RawGeometry, 1, 15, 25);

        // Baked = Ctm.Apply(raw): (2*5+10, 2*5+20)=(20,30); (2*15+10, 2*25+20)=(40,70).
        AssertPoint(path.Geometry, 0, 20, 30);
        AssertPoint(path.Geometry, 1, 40, 70);

        AssertRoundTrips(path);
    }

    [Fact]
    public void IdentityCtm_RawEqualsBaked()
    {
        using PdfDocument doc = BuildPageWithContent("5 5 m\n15 5 l\nS");

        PathOp path = SinglePath(doc);

        path.Ctm.Should().Be(AffineMatrix.Identity);
        path.RawGeometry.Should().NotBeNull();
        AssertPoint(path.RawGeometry!, 0, 5, 5);
        AssertPoint(path.Geometry, 0, 5, 5);
        AssertPoint(path.RawGeometry!, 1, 15, 5);
        AssertPoint(path.Geometry, 1, 15, 5);
    }

    [Fact]
    public void Rectangle_UnderScale_RetainsRawCorners()
    {
        using PdfDocument doc = BuildPageWithContent("2 0 0 2 0 0 cm\n10 10 20 20 re\nf");

        PathOp path = SinglePath(doc);

        path.RawGeometry.Should().NotBeNull();
        // re emits MoveTo, 3x LineTo, Close.
        path.RawGeometry!.Segments.Select(s => s.Command).Should()
            .Equal(PathCommand.MoveTo, PathCommand.LineTo, PathCommand.LineTo,
                   PathCommand.LineTo, PathCommand.Close);
        AssertPoint(path.RawGeometry, 0, 10, 10);
        AssertPoint(path.RawGeometry, 1, 30, 10);
        AssertPoint(path.RawGeometry, 2, 30, 30);
        AssertPoint(path.RawGeometry, 3, 10, 30);
        AssertPoint(path.Geometry, 2, 60, 60);

        AssertRoundTrips(path);
    }

    [Fact]
    public void CubicCurve_UnderScale_RoundTripsAllControlPoints()
    {
        using PdfDocument doc = BuildPageWithContent(
            "2 0 0 2 0 0 cm\n0 0 m\n10 0 10 10 0 10 c\nS");

        PathOp path = SinglePath(doc);

        path.RawGeometry.Should().NotBeNull();
        PathSegment cubic = path.RawGeometry!.Segments.Single(s => s.Command == PathCommand.CubicTo);
        cubic.X1.Should().BeApproximately(10, 1e-9);
        cubic.Y1.Should().BeApproximately(0, 1e-9);
        cubic.X2.Should().BeApproximately(10, 1e-9);
        cubic.Y2.Should().BeApproximately(10, 1e-9);
        cubic.X3.Should().BeApproximately(0, 1e-9);
        cubic.Y3.Should().BeApproximately(10, 1e-9);

        AssertRoundTrips(path);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static PathOp SinglePath(PdfDocument doc)
    {
        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        return list.OfType<PathOp>().Single();
    }

    private static void AssertPoint(PathGeometry geom, int index, double x, double y)
    {
        PathSegment seg = geom.Segments[index];
        seg.X1.Should().BeApproximately(x, 1e-9);
        seg.Y1.Should().BeApproximately(y, 1e-9);
    }

    private static void AssertRoundTrips(PathOp path)
    {
        PathGeometry raw = path.RawGeometry!;
        for (int i = 0; i < raw.Segments.Count; i++)
        {
            PathSegment r = raw.Segments[i];
            PathSegment b = path.Geometry.Segments[i];
            r.Command.Should().Be(b.Command);
            if (r.Command is PathCommand.MoveTo or PathCommand.LineTo)
            {
                (double bx, double by) = path.Ctm.Apply(r.X1, r.Y1);
                bx.Should().BeApproximately(b.X1, 1e-9);
                by.Should().BeApproximately(b.Y1, 1e-9);
            }
            else if (r.Command == PathCommand.CubicTo)
            {
                (double bx3, double by3) = path.Ctm.Apply(r.X3, r.Y3);
                bx3.Should().BeApproximately(b.X3, 1e-9);
                by3.Should().BeApproximately(b.Y3, 1e-9);
            }
        }
    }

    // Builds a one-page PDF whose single content stream is the given operators.
    private static PdfDocument BuildPageWithContent(string content)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(contentDict, contentBytes);

        PdfIndirectObject[] objects =
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
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
